using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.NextcloudOAuth2.Configuration;
using Jellyfin.Plugin.NextcloudOAuth2.Models;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.NextcloudOAuth2.Sso.SsoOptions;

namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// HTTP-клиент Nextcloud: OIDC discovery (с кэшем), обмен code на токен
/// и получение данных пользователя через OCS API.
/// </summary>
/// <remarks>
/// Логгер передаётся вызывающей стороной, поэтому категория журнала
/// определяется создателем экземпляра, а не этим классом.
/// </remarks>
internal sealed class NextcloudApiClient
{
    private static readonly object DiscoveryLock = new();
    private static OIDCDiscovery? _discoveryCache;
    private static DateTime _discoveryCacheTime = DateTime.MinValue;
    private static bool _discoveryCachePopulated;

    private readonly ILogger _logger;

    public NextcloudApiClient(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Получает OIDC discovery документ (с кэшем). Недоступность discovery —
    /// не ошибка: возвращается null и используется fallback-endpoint Nextcloud.
    /// Сетевые ошибки пробрасываются вызывающему коду.
    /// </summary>
    public async Task<OIDCDiscovery?> GetDiscoveryAsync(HttpClient client, PluginConfiguration config)
    {
        lock (DiscoveryLock)
        {
            if (_discoveryCachePopulated && DateTime.UtcNow - _discoveryCacheTime < DiscoveryCacheTtl)
            {
                return _discoveryCache;
            }
        }

        var discoveryUrl = $"{config.NextcloudServerUrl.TrimEnd('/')}/.well-known/openid-configuration";

        OIDCDiscovery? result;
        try
        {
            using var cts = new CancellationTokenSource(UpstreamRequestTimeout);
            var response = await client.GetAsync(discoveryUrl, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                result = JsonSerializer.Deserialize<OIDCDiscovery>(json, SsoOptions.JsonOptions);
                if (string.IsNullOrEmpty(result?.AuthorizationEndpoint))
                {
                    result = null;
                }
            }
            else
            {
                // Например, 404 — Nextcloud без OIDC discovery. Это нормальный кейс.
                _logger.LogDebug(
                    "[NextcloudOAuth2] OIDC discovery недоступен ({StatusCode}), используем fallback",
                    response.StatusCode);
                result = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw new HttpRequestException("Таймаут запроса OIDC discovery");
        }

        lock (DiscoveryLock)
        {
            _discoveryCache = result;
            _discoveryCacheTime = DateTime.UtcNow;
            _discoveryCachePopulated = true;
        }

        return result;
    }

    /// <summary>
    /// Обменивает authorization code на access token.
    /// </summary>
    public async Task<TokenResponse?> ExchangeCodeForToken(
        HttpClient client,
        string code,
        string redirectUri,
        PluginConfiguration config,
        string? codeVerifier,
        OIDCDiscovery? discovery)
    {
        var tokenEndpoint = discovery?.TokenEndpoint
            ?? $"{config.NextcloudServerUrl.TrimEnd('/')}/apps/oauth2/api/v1/token";

        var formData = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("client_id", config.ClientId),
            new("client_secret", config.ClientSecret)
        };

        if (codeVerifier != null)
        {
            formData.Add(new("code_verifier", codeVerifier));
        }

        using var cts = new CancellationTokenSource(UpstreamRequestTimeout);
        var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(formData), cts.Token)
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Тело ошибки token endpoint не содержит токенов — логируем для диагностики
            _logger.LogError(
                "[NextcloudOAuth2] Token endpoint вернул {StatusCode}: {Content}",
                response.StatusCode,
                Truncate(content, 500));
            throw new HttpRequestException($"Token endpoint вернул {response.StatusCode}");
        }

        // Успешный ответ содержит токены — логируем только факт, без телa
        _logger.LogDebug("[NextcloudOAuth2] Token endpoint: успех, тело не логируется по соображениям безопасности");

        return JsonSerializer.Deserialize<TokenResponse>(content, JsonOptions);
    }

    /// <summary>
    /// Получает информацию о пользователе через Nextcloud OCS API.
    /// </summary>
    public async Task<UserInfo?> GetUserInfo(
        HttpClient client,
        string accessToken,
        PluginConfiguration config)
    {
        var userInfoEndpoint = $"{config.NextcloudServerUrl.TrimEnd('/')}/ocs/v2.php/cloud/user?format=json";

        var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        // Nextcloud требует специальный заголовок для OCS API
        request.Headers.Add("OCS-APIRequest", "true");

        using var cts = new CancellationTokenSource(UpstreamRequestTimeout);
        var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "[NextcloudOAuth2] UserInfo endpoint вернул {StatusCode}: {Content}",
                response.StatusCode,
                Truncate(content, 500));
            throw new HttpRequestException($"UserInfo endpoint вернул {response.StatusCode}");
        }

        // Ответ OCS содержит PII (email, имя) — логируем только факт
        _logger.LogDebug("[NextcloudOAuth2] UserInfo: получен ответ, тело не логируется");

        var wrapper = JsonSerializer.Deserialize<NextcloudUserWrapper>(content, SsoOptions.JsonOptions);

        if (wrapper?.Ocs?.Data == null)
        {
            return null;
        }

        return new UserInfo
        {
            // OCS возвращает id (uid) — стабильный идентификатор пользователя Nextcloud
            UserId = wrapper.Ocs.Data.Id,
            PreferredUsername = wrapper.Ocs.Data.Id,
            Email = wrapper.Ocs.Data.Email,
            Name = wrapper.Ocs.Data.DisplayName
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
