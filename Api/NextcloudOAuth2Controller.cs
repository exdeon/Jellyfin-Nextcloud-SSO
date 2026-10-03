// Включаем контекст аннотаций null-безопасности
// Это убирает предупреждения CS8632 для string?
#nullable enable

using System;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
// Этот импорт нужен для Task<> в асинхронных методах
using System.Threading.Tasks;
using Jellyfin.Plugin.NextcloudOAuth2.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NextcloudOAuth2.Api;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)] // Только администраторы
[Route("NextcloudOAuth2")]
[Produces(MediaTypeNames.Application.Json)]
public class NextcloudOAuth2Controller : ControllerBase
{
    private readonly Plugin _plugin;
    private readonly ILogger<NextcloudOAuth2Controller> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public NextcloudOAuth2Controller(
        ILogger<NextcloudOAuth2Controller> logger,
        IHttpClientFactory httpClientFactory)
    {
        _plugin = Plugin.Instance
            ?? throw new InvalidOperationException("Экземпляр плагина NextcloudOAuth2 ещё не инициализирован");
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// GET: Возвращает текущие настройки плагина (ClientSecret маскируется).
    /// </summary>
    [HttpGet("Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginConfiguration> GetConfiguration()
    {
        // Не отдаём секрет на клиент — он хранится открытым XML на сервере
        var config = _plugin.Configuration;
        return new PluginConfiguration
        {
            NextcloudServerUrl = config.NextcloudServerUrl,
            ClientId = config.ClientId,
            ClientSecret = string.Empty,
            AutoCreateUsers = config.AutoCreateUsers,
            BindExistingUsersByName = config.BindExistingUsersByName,
            EnablePkce = config.EnablePkce,
            PublicBaseUrl = config.PublicBaseUrl,
            UserLinks = config.UserLinks
        };
    }

    /// <summary>
    /// POST: Сохраняет новые настройки плагина.
    /// Пустой ClientSecret означает «не изменён» — сохраняется прежнее значение.
    /// </summary>
    [HttpPost("Configuration")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult UpdateConfiguration([FromBody] PluginConfiguration newConfig)
    {
        if (string.IsNullOrEmpty(newConfig.ClientSecret))
        {
            newConfig.ClientSecret = _plugin.Configuration.ClientSecret;
        }

        // Клиент не редактирует привязки — пустой список означает «не изменён»
        if (newConfig.UserLinks == null || newConfig.UserLinks.Count == 0)
        {
            newConfig.UserLinks = _plugin.Configuration.UserLinks;
        }

        _plugin.UpdateConfiguration(newConfig);
        return NoContent();
    }

    /// <summary>
    /// Тестирует подключение к Nextcloud серверу через status.php.
    /// </summary>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestConnectionResult>> TestConnection()
    {
        var config = _plugin.Configuration;

        if (string.IsNullOrWhiteSpace(config.NextcloudServerUrl))
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                ErrorMessage = Localization.Text(HttpContext, "test.urlMissing")
            });
        }

        var statusUrl = $"{config.NextcloudServerUrl.TrimEnd('/')}/status.php";
        _logger.LogInformation("[NextcloudOAuth2] Тестирование подключения к: {Url}", statusUrl);

        try
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);

            // Увеличиваем таймаут для диагностики
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(statusUrl);

            _logger.LogInformation("[NextcloudOAuth2] Ответ от Nextcloud: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                // Обрезаем тело ответа: оно приходит от внешнего сервера и не должно
                // засорять UI/журнал неограниченным объёмом
                if (errorContent.Length > 300)
                {
                    errorContent = errorContent[..300] + "...";
                }

                _logger.LogWarning("[NextcloudOAuth2] Ошибка HTTP: {StatusCode}, Тело: {Content}",
                    response.StatusCode, errorContent);

                return Ok(new TestConnectionResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {response.StatusCode}: {errorContent}"
                });
            }

            var content = await response.Content.ReadAsStringAsync();
            // Ответ приходит от внешнего сервера — логируем ограниченный фрагмент
            var logged = content.Length > 1000 ? content[..1000] + "..." : content;
            _logger.LogDebug("[NextcloudOAuth2] Ответ Nextcloud: {Content}", logged);

            var status = JsonSerializer.Deserialize<NextcloudStatus>(content);

            if (status?.Installed == true)
            {
                _logger.LogInformation("[NextcloudOAuth2] Подключение успешно! Версия Nextcloud: {Version}",
                    status.Version);

                return Ok(new TestConnectionResult
                {
                    Success = true,
                    ServerVersion = status.Version ?? "Unknown"
                });
            }

            return Ok(new TestConnectionResult
            {
                Success = false,
                ErrorMessage = Localization.Text(HttpContext, "test.notInstalled")
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Ошибка HTTP-запроса к Nextcloud");
            return Ok(new TestConnectionResult
            {
                Success = false,
                ErrorMessage = Localization.Text(HttpContext, "test.networkError", ex.Message)
            });
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Таймаут подключения к Nextcloud");
            return Ok(new TestConnectionResult
            {
                Success = false,
                ErrorMessage = Localization.Text(HttpContext, "test.timeout")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NextcloudOAuth2] Непредвиденная ошибка при подключении к Nextcloud");
            return Ok(new TestConnectionResult
            {
                Success = false,
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
            });
        }
    }
}

/// <summary>
/// Результат тестирования подключения.
/// </summary>
public class TestConnectionResult
{
    public bool Success { get; set; }
    public string? ServerVersion { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Модель ответа Nextcloud /status.php
/// </summary>
public class NextcloudStatus
{
    // Маппим "installed" из JSON на Installed в C#
    [JsonPropertyName("installed")]
    public bool Installed { get; set; }

    // Маппим "version" из JSON на Version в C#
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}