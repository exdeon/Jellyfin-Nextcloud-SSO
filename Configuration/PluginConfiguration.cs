using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NextcloudOAuth2.Configuration;

/// <summary>
/// Класс для хранения настроек плагина.
/// Jellyfin автоматически сериализует его в XML-файл на диске.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Базовый URL вашего Nextcloud (например, https://cloud.example.com).
    /// </summary>
    public string NextcloudServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Client ID, полученный в настройках OAuth2 Nextcloud.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client Secret.
    /// ВНИМАНИЕ: Jellyfin хранит конфигурацию плагина обычным открытым XML-файлом
    /// без шифрования — к секрету должен быть ограничен доступ на уровне ФС/контейнера.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Создавать новый аккаунт Jellyfin автоматически при первом входе
    /// пользователя, у которого ещё нет привязки.
    /// </summary>
    public bool AutoCreateUsers { get; set; } = true;

    /// <summary>
    /// Разрешить одноразовую привязку по совпадению имени пользователя
    /// (миграция со старых версий плагина). После миграции всех пользователей
    /// рекомендуется отключить — иначе возможен захват чужого аккаунта
    /// при совпадении имён.
    /// </summary>
    public bool BindExistingUsersByName { get; set; } = true;

    /// <summary>
    /// Включить PKCE (S256) в OAuth2 flow. Требует поддержки PKCE на сервере Nextcloud.
    /// </summary>
    public bool EnablePkce { get; set; }

    /// <summary>
    /// Публичный базовый URL сервера Jellyfin (например https://jellyfin.example.com).
    /// Если задан, используется вместо URL из запроса — необходимо при работе
    /// за reverse-proxy без настроенных forwarded headers.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Привязки стабильного идентификатора Nextcloud к пользователям Jellyfin.
    /// </summary>
    public List<UserLink> UserLinks { get; set; } = new List<UserLink>();
}

/// <summary>
/// Привязка пользователя Nextcloud к пользователю Jellyfin по стабильному ID.
/// </summary>
public class UserLink
{
    /// <summary>
    /// Стабильный (неизменяемый) идентификатор пользователя в Nextcloud.
    /// </summary>
    public string NextcloudUserId { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор связанного пользователя Jellyfin.
    /// </summary>
    public Guid JellyfinUserId { get; set; }
}
