using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.NextcloudOAuth2.Configuration;
using Jellyfin.Plugin.NextcloudOAuth2.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// Сопоставление пользователя Nextcloud с пользователем Jellyfin:
/// стабильная привязка → (опционально) имя → (опционально) автосоздание.
/// </summary>
internal sealed class UserBinder
{
    private readonly Plugin _plugin;
    private readonly ILogger _logger;
    private readonly IUserManager _userManager;

    public UserBinder(Plugin plugin, ILogger logger, IUserManager userManager)
    {
        _plugin = plugin;
        _logger = logger;
        _userManager = userManager;
    }

    /// <summary>
    /// Определяет пользователя Jellyfin: по стабильной привязке, затем
    /// (опционально) по совпадению имени, затем (опционально) создаёт нового.
    /// </summary>
    public async Task<User?> ResolveAsync(UserInfo userInfo, PluginConfiguration config)
    {
        // 1. Стабильная привязка Nextcloud ID → Jellyfin user id
        var link = config.UserLinks.FirstOrDefault(
            l => string.Equals(l.NextcloudUserId, userInfo.UserId, StringComparison.Ordinal));

        if (link != null)
        {
            var linked = _userManager.GetUserById(link.JellyfinUserId);
            if (linked != null)
            {
                return linked;
            }

            // Привязка ссылается на удалённого пользователя — удаляем её
            _logger.LogWarning(
                "[NextcloudOAuth2] Привязка {NextcloudId} ссылается на несуществующего пользователя {UserId}, удаляется",
                link.NextcloudUserId,
                link.JellyfinUserId);
            lock (SsoLock.Root)
            {
                config.UserLinks.Remove(link);
            }

            _plugin.UpdateConfiguration(config);
        }

        // 2. Legacy: привязка по совпадению имени (для миграции со старых версий)
        if (config.BindExistingUsersByName)
        {
            var lookupName = userInfo.PreferredUsername ?? userInfo.UserId;
            if (!string.IsNullOrEmpty(lookupName))
            {
                var byName = _userManager.GetUserByName(lookupName);
                if (byName != null)
                {
                    _logger.LogInformation(
                        "[NextcloudOAuth2] Миграционная привязка по имени {User} → {UserId}",
                        lookupName,
                        byName.Id);
                    AddLink(config, userInfo.UserId!, byName.Id);
                    return byName;
                }
            }
        }

        // 3. Автосоздание нового аккаунта
        if (config.AutoCreateUsers)
        {
            try
            {
                _logger.LogInformation(
                    "[NextcloudOAuth2] Создание нового пользователя: {User}",
                    userInfo.PreferredUsername);
                var created = await _userManager.CreateUserAsync(userInfo.PreferredUsername!).ConfigureAwait(false);
                AddLink(config, userInfo.UserId!, created.Id);
                return created;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NextcloudOAuth2] Не удалось создать пользователя {User}", userInfo.PreferredUsername);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Записывает/обновляет привязка Nextcloud ID → Jellyfin user id и сохраняет конфигурацию.
    /// </summary>
    public void AddLink(PluginConfiguration config, string nextcloudUserId, Guid jellyfinUserId)
    {
        lock (SsoLock.Root)
        {
            var existing = config.UserLinks.FirstOrDefault(
                l => string.Equals(l.NextcloudUserId, nextcloudUserId, StringComparison.Ordinal));
            if (existing != null)
            {
                if (existing.JellyfinUserId != jellyfinUserId)
                {
                    existing.JellyfinUserId = jellyfinUserId;
                }
                else
                {
                    return;
                }
            }
            else
            {
                config.UserLinks.Add(new UserLink
                {
                    NextcloudUserId = nextcloudUserId,
                    JellyfinUserId = jellyfinUserId
                });
            }
        }

        _plugin.UpdateConfiguration(config);
    }
}
