using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.NextcloudOAuth2.Configuration;

namespace Jellyfin.Plugin.NextcloudOAuth2;

/// <summary>
/// Основной класс плагина Nextcloud OAuth2.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Уникальный идентификатор плагина. Обязан совпадать с meta.json и JS.
    /// </summary>
    public static readonly Guid PluginGuid = new Guid("5c4de6ba-220f-4ace-9dce-72ba60c66861");

    /// <summary>
    /// Конструктор плагина. Вызывается DI-контейнером Jellyfin.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        // Сохраняем глобальный экземпляр для доступа из контроллеров и сервисов
        Instance = this;
    }

    /// <summary>
    /// Глобальный синглтон экземпляра плагина.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Отображаемое имя в админке Jellyfin.
    /// </summary>
    public override string Name => "Nextcloud OAuth2";

    /// <summary>
    /// Переопределение ID для корректной идентификации Jellyfin.
    /// </summary>
    public override Guid Id => PluginGuid;

    /// <summary>
    /// Регистрация HTML и JS страниц для UI Jellyfin (React-дашборд).
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "OAuth2SSOConfig",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                // Имя должно совпадать с тем, что указано в data-controller (без __plugin/)
                Name = "configPage.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Web.configPage.js"
            }
        };
    }

    // Добавляем удобное статическое свойство для быстрого доступа к настройкам из любого места
    public PluginConfiguration Config => Configuration;
}