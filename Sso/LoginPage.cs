using System.Text.Json;

namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// HTML-страница, отдающаяся после успешного входа: сохраняет токен
/// в localStorage в формате, который ожидает Jellyfin Web (модуль Credentials),
/// и перенаправляет пользователя.
/// </summary>
internal static class LoginPage
{
    /// <summary>
    /// Рендерит страницу установки токена. Значения встраиваются
    /// через JSON-сериализацию (защита от инъекции).
    /// </summary>
    public static string Render(
        string baseUrl,
        string accessToken,
        string serverId,
        string userId,
        string returnUrl)
    {
        var credentialsJson = JsonSerializer.Serialize(new
        {
            Servers = new[]
            {
                new
                {
                    Id = serverId,
                    ManualAddress = baseUrl,
                    LastConnectionMode = 2,
                    DateLastAccessed = 0L,
                    UserId = userId,
                    AccessToken = accessToken,
                    Name = "Jellyfin Server",
                    LocalAddress = baseUrl,
                    RemoteAddress = baseUrl
                }
            }
        });

        var returnUrlJson = JsonSerializer.Serialize(returnUrl);

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Вход через Nextcloud</title>
    <style>
        body {{ font-family: sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #101010; color: #fff; }}
        .spinner {{ width: 40px; height: 40px; border: 4px solid rgba(255,255,255,0.3); border-top-color: #00a4dc; border-radius: 50%; animation: spin 1s linear infinite; margin: 0 auto 1em; }}
        @keyframes spin {{ to {{ transform: rotate(360deg); }} }}
    </style>
</head>
<body>
    <div style='text-align:center'>
        <div class='spinner'></div>
        <p>Вход выполнен, перенаправление...</p>
    </div>
    <script>
        try {{
            var credentials = {credentialsJson};
            credentials.Servers[0].DateLastAccessed = Date.now();

            // Сохраняем в localStorage под ключом, который использует фронтенд
            localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));

            console.log('[NextcloudOAuth2] jellyfin_credentials установлен');

            // Перенаправляем на запрошенный адрес (только локальный путь)
            setTimeout(function () {{
                window.location.replace(window.location.origin + {returnUrlJson});
            }}, 300);
        }} catch (e) {{
            console.error('[NextcloudOAuth2] Ошибка:', e);
            window.location.replace(window.location.origin + '/web/index.html#!/login');
        }}
    </script>
</body>
</html>";
    }
}
