using System;
using System.Text.Json;

namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// Константы и параметры SSO-flow, общие для контроллера и сервисов.
/// </summary>
internal static class SsoOptions
{
    /// <summary>
    /// Имя cookie с hash(state) — защита от login CSRF.
    /// </summary>
    internal const string StateCookieName = "nf_sso_state";

    /// <summary>
    /// Имя cookie со стабильным DeviceId браузера.
    /// </summary>
    internal const string DeviceCookieName = "nf_sso_did";

    /// <summary>
    /// Ограничение размера хранилища state (защита от исчерпания памяти).
    /// </summary>
    internal const int MaxStateEntries = 1000;

    /// <summary>
    /// Страница, на которую возвращаемся, если returnUrl не задан или отклонён.
    /// </summary>
    internal const string DefaultReturnUrl = "/web/index.html#!/home";

    /// <summary>
    /// Время жизни state и cookie привязки.
    /// </summary>
    internal static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// TTL кэша OIDC discovery документа.
    /// </summary>
    internal static readonly TimeSpan DiscoveryCacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Таймаут запросов к Nextcloud (discovery, token, userinfo).
    /// </summary>
    internal static readonly TimeSpan UpstreamRequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// JSON-настройки для разбора ответов Nextcloud.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}
