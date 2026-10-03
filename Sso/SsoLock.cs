namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// Единый lock проекта. Им пользуются и state-хранилище, и мутации
/// <c>PluginConfiguration.UserLinks</c> — как и раньше, когда оба места
/// делили один статический объект <c>StateLock</c> в контроллере.
/// </summary>
internal static class SsoLock
{
    internal static readonly object Root = new();
}
