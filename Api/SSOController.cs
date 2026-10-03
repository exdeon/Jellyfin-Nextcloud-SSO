using System;
using System.Net.Http;
using System.Security.Cryptography;

using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.NextcloudOAuth2.Configuration;
using Jellyfin.Plugin.NextcloudOAuth2.Models;
using Jellyfin.Plugin.NextcloudOAuth2.Sso;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.NextcloudOAuth2.Sso.SsoOptions;

namespace Jellyfin.Plugin.NextcloudOAuth2.Api;

/// <summary>
/// Контроллер для обработки OAuth2/OIDC flow с Nextcloud.
/// </summary>
[ApiController]
[AllowAnonymous] // Эти эндпоинты должны быть доступны без авторизации
[Route("sso")]
public class SSOController : ControllerBase
{
    // Константы, TTL и JSON-настройки flow — в SsoOptions (подключены using static),
    // state и cookie-привязка — в Sso.StateStore, HTTP к Nextcloud — в Sso.NextcloudApiClient,
    // привязка пользователей — в Sso.UserBinder, страница выдачи токена — в Sso.LoginPage.

    private readonly Plugin _plugin;
    private readonly ILogger<SSOController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly NextcloudApiClient _nextcloud;
    private readonly UserBinder _userBinder;

    public SSOController(
        ILogger<SSOController> logger,
        IHttpClientFactory httpClientFactory,
        IUserManager userManager,
        ISessionManager sessionManager)
    {
        _plugin = Plugin.Instance
            ?? throw new InvalidOperationException("Экземпляр плагина NextcloudOAuth2 ещё не инициализирован");
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _nextcloud = new NextcloudApiClient(logger);
        _userBinder = new UserBinder(_plugin, logger, userManager);
    }

    /// <summary>
    /// Шаг 1: Инициирует OAuth2 flow. Редиректит пользователя на Nextcloud.
    /// </summary>
    [HttpGet("OID/start/nextcloud")]
    public async Task<ActionResult> StartOAuth(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? deviceId = null)
    {
        var config = _plugin.Configuration;

        if (string.IsNullOrWhiteSpace(config.NextcloudServerUrl) ||
            string.IsNullOrWhiteSpace(config.ClientId))
        {
            return BadRequest(Localization.Text(HttpContext, "sso.notConfigured"));
        }

        var finalReturnUrl = SanitizeReturnUrl(returnUrl);
        var resolvedDeviceId = ResolveDeviceId(deviceId);

        // Генерируем случайный state для защиты от CSRF
        var state = StateStore.NewState();

        // PKCE (S256) — опционально
        string? codeVerifier = null;
        string? codeChallenge = null;
        if (config.EnablePkce)
        {
            codeVerifier = StateStore.NewState();
            codeChallenge = Convert.ToBase64String(
                SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        StateStore.Store(state, new StateEntry
        {
            ExpiresAtUtc = DateTime.UtcNow.Add(StateLifetime),
            DeviceId = resolvedDeviceId,
            ReturnUrl = finalReturnUrl,
            CodeVerifier = codeVerifier
        });

        // Привязка state к браузеру (защита от login CSRF)
        StateStore.SetBindingCookie(HttpContext, state);

        var redirectUri = $"{GetBaseUrl()}/sso/OID/redirect/nextcloud";
        var client = _httpClientFactory.CreateClient(NamedClient.Default);

        OIDCDiscovery? discovery;
        try
        {
            discovery = await _nextcloud.GetDiscoveryAsync(client, config).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Ошибка подключения при получении OIDC discovery");
            StateStore.Remove(state);
            return BadRequest(Localization.Text(HttpContext, "sso.connectionFailed"));
        }

        // Fallback на стандартный endpoint Nextcloud, если discovery недоступен
        var authorizeEndpoint = discovery?.AuthorizationEndpoint
            ?? $"{config.NextcloudServerUrl.TrimEnd('/')}/apps/oauth2/authorize";

        var authorizeUrl = authorizeEndpoint +
            $"?client_id={Uri.EscapeDataString(config.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid profile email")}" +
            $"&state={Uri.EscapeDataString(state)}";

        if (codeChallenge != null)
        {
            authorizeUrl += $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                "&code_challenge_method=S256";
        }

        _logger.LogInformation("[NextcloudOAuth2] Redirect to: {Url}", authorizeUrl);

        return Redirect(authorizeUrl);
    }

    /// <summary>
    /// Шаг 2: Callback от Nextcloud с authorization code.
    /// Этот URI вы указывали в настройках Nextcloud.
    /// </summary>
    [HttpGet("OID/redirect/nextcloud")]
    public async Task<ActionResult> HandleCallback(
        [FromQuery] string? code,
        [FromQuery] string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            _logger.LogWarning("[NextcloudOAuth2] Отсутствует state параметр");
            return BadRequest(Localization.Text(HttpContext, "sso.invalidState"));
        }

        // Проверяем привязку state к браузеру (login CSRF)
        if (!StateStore.VerifyBinding(HttpContext, state))
        {
            _logger.LogWarning("[NextcloudOAuth2] State не совпадает с cookie браузера");
            return BadRequest(Localization.Text(HttpContext, "sso.invalidState"));
        }

        // Проверяем state в хранилище (CSRF + одноразовость)
        var entry = StateStore.Take(state);
        if (entry == null)
        {
            _logger.LogWarning("[NextcloudOAuth2] Неверный или истекший state");
            return BadRequest(Localization.Text(HttpContext, "sso.invalidState"));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(Localization.Text(HttpContext, "sso.missingCode"));
        }

        var config = _plugin.Configuration;

        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            var redirectUri = $"{GetBaseUrl()}/sso/OID/redirect/nextcloud";

            // Шаг 3: Обмениваем code на access token
            var discovery = await _nextcloud.GetDiscoveryAsync(client, config).ConfigureAwait(false);
            var tokenResponse = await _nextcloud.ExchangeCodeForToken(
                client, code, redirectUri, config, entry.CodeVerifier, discovery).ConfigureAwait(false);

            if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
            {
                _logger.LogWarning("[NextcloudOAuth2] Пустой access token в ответе token endpoint");
                return BadRequest(Localization.Text(HttpContext, "sso.tokenFailed"));
            }

            // Шаг 4: Получаем информацию о пользователе
            var userInfo = await _nextcloud.GetUserInfo(client, tokenResponse.AccessToken, config).ConfigureAwait(false);

            if (userInfo == null || string.IsNullOrEmpty(userInfo.PreferredUsername))
            {
                _logger.LogWarning("[NextcloudOAuth2] Не удалось получить информацию о пользователе");
                return BadRequest(Localization.Text(HttpContext, "sso.userInfoFailed"));
            }

            _logger.LogInformation(
                "[NextcloudOAuth2] Аутентифицирован пользователь: {User}",
                userInfo.PreferredUsername);

            // Шаг 5: Находим/привязываем/создаём пользователя и выполняем вход
            return await AuthenticateUser(userInfo, config, entry.DeviceId, entry.ReturnUrl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Ошибка при обработке callback");
            return BadRequest(Localization.Text(HttpContext, "sso.authError"));
        }
    }

    /// <summary>
    /// Находит пользователя по стабильной привязке, при необходимости создаёт его,
    /// записывает привязку и выполняет вход через штатный ISessionManager.
    /// </summary>
    private async Task<ActionResult> AuthenticateUser(
        UserInfo userInfo,
        PluginConfiguration config,
        string deviceId,
        string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(userInfo.UserId))
        {
            _logger.LogWarning("[NextcloudOAuth2] Стабильный ID пользователя из Nextcloud пустой");
            return BadRequest(Localization.Text(HttpContext, "sso.noUserId"));
        }

        var user = await _userBinder.ResolveAsync(userInfo, config).ConfigureAwait(false);

        if (user == null)
        {
            _logger.LogWarning(
                "[NextcloudOAuth2] Вход отклонён: для {NextcloudId} нет привязки, автосоздание отключено",
                userInfo.UserId);
            return BadRequest(Localization.Text(HttpContext, "sso.accountNotFound"));
        }

        _logger.LogInformation("[NextcloudOAuth2] Вход выполнен для пользователя: {UserId}", user.Id);

        try
        {
            var authResult = await _sessionManager.AuthenticateDirect(new AuthenticationRequest
            {
                UserId = user.Id,
                App = "Nextcloud OAuth2",
                AppVersion = "1.0.0",
                DeviceId = deviceId,
                DeviceName = "Browser",
                RemoteEndPoint = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
            }).ConfigureAwait(false);

            _logger.LogInformation("[NextcloudOAuth2] Access token создан, перенаправляем на главную");

            // Страница с токеном не должна кешироваться
            Response.Headers.CacheControl = "no-store";

            return Content(
                LoginPage.Render(GetBaseUrl(), authResult.AccessToken, authResult.ServerId, user.Id.ToString(), returnUrl),
                "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Ошибка создания сессии для пользователя {UserId}", user.Id);
            return BadRequest(Localization.Text(HttpContext, "sso.sessionFailed"));
        }
    }

    /// <summary>
    /// Определяет DeviceId flow: из query → из cookie (стабильный для браузера) → новый.
    /// </summary>
    private string ResolveDeviceId(string? queryDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(queryDeviceId) && queryDeviceId.Length <= 128)
        {
            return queryDeviceId;
        }

        if (Request.Cookies.TryGetValue(DeviceCookieName, out var cookieDeviceId) &&
            !string.IsNullOrWhiteSpace(cookieDeviceId) &&
            cookieDeviceId.Length <= 128)
        {
            return cookieDeviceId;
        }

        var newDeviceId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(DeviceCookieName, newDeviceId, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365)
        });
        return newDeviceId;
    }

    /// <summary>
    /// Допускает только локальные пути (/...), запрещая open redirect (//host, /\host).
    /// </summary>
    private static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return DefaultReturnUrl;
        }

        var value = returnUrl.Trim();
        if (value.Length > 2048 ||
            !value.StartsWith('/') ||
            (value.Length > 1 && (value[1] == '/' || value[1] == '\\')) ||
            value.Contains('\\'))
        {
            return DefaultReturnUrl;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return DefaultReturnUrl;
            }
        }

        return value;
    }

    /// <summary>
    /// Получает базовый URL текущего сервера Jellyfin (с учётом PublicBaseUrl).
    /// </summary>
    private string GetBaseUrl()
    {
        var configured = _plugin.Configuration.PublicBaseUrl;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var trimmed = configured.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return trimmed;
            }

            _logger.LogWarning("[NextcloudOAuth2] PublicBaseUrl некорректен, используется URL запроса: {Url}", configured);
        }

        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }

}
