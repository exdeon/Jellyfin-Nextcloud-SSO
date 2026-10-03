#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.NextcloudOAuth2;

/// <summary>
/// Локализация пользовательских сообщений плагина (ru/en).
/// Индекс 0 — русский, индекс 1 — английский. Язык выбирается по заголовку
/// Accept-Language запроса: первый тег вида ru-RU даёт русский, всё остальное — английский.
/// </summary>
internal static class Localization
{
    private static readonly Dictionary<string, string[]> Messages = new(StringComparer.Ordinal)
    {
        // Ошибки SSO-flow (показываются браузеру как 400 Bad Request)
        ["sso.notConfigured"] = new[] { "Nextcloud OAuth2 не настроен", "Nextcloud OAuth2 is not configured" },
        ["sso.connectionFailed"] = new[] { "Не удалось подключиться к Nextcloud", "Could not connect to Nextcloud" },
        ["sso.invalidState"] = new[] { "Неверный state параметр", "Invalid state parameter" },
        ["sso.missingCode"] = new[] { "Отсутствует code параметр", "Missing code parameter" },
        ["sso.tokenFailed"] = new[] { "Не удалось получить access token. Подробности в журнале сервера.", "Could not obtain an access token. See the server log for details." },
        ["sso.userInfoFailed"] = new[] { "Не удалось получить информацию о пользователе. Подробности в журнале сервера.", "Could not fetch user information. See the server log for details." },
        ["sso.authError"] = new[] { "Ошибка аутентификации. Подробности в журнале сервера.", "Authentication error. See the server log for details." },
        ["sso.noUserId"] = new[] { "Не удалось получить идентификатор пользователя из Nextcloud", "Could not read the user identifier from Nextcloud" },
        ["sso.accountNotFound"] = new[] { "Аккаунт не найден. Обратитесь к администратору.", "Account not found. Contact your administrator." },
        ["sso.sessionFailed"] = new[] { "Не удалось создать сессию. Подробности в журнале сервера.", "Could not create a session. See the server log for details." },

        // Результат «Проверить подключение» на странице настроек
        ["test.urlMissing"] = new[] { "URL сервера не указан", "Server URL is not set" },
        ["test.notInstalled"] = new[] { "Nextcloud не установлен или ответ некорректен", "Nextcloud is not installed or the response is invalid" },
        ["test.networkError"] = new[] { "Ошибка сети: {0}. Проверьте, что URL доступен из контейнера Jellyfin.", "Network error: {0}. Check that the URL is reachable from the Jellyfin container." },
        ["test.timeout"] = new[] { "Таймаут подключения (10 сек). Сервер недоступен или отвечает слишком медленно.", "Connection timed out after 10 s. The server is unreachable or responds too slowly." }
    };

    public static string Text(HttpContext context, string key)
    {
        return Resolve(context, key);
    }

    public static string Text(HttpContext context, string key, params object?[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, Resolve(context, key), args);
    }

    private static string Resolve(HttpContext context, string key)
    {
        if (!Messages.TryGetValue(key, out var texts))
        {
            return key;
        }

        return IsRussian(context) ? texts[0] : texts[1];
    }

    private static bool IsRussian(HttpContext context)
    {
        var header = context.Request.Headers["Accept-Language"].ToString();

        // Браузеры перечисляют языки по убыванию приоритета: достаточно первого тега
        var first = header.Split(',')[0];
        var culture = first.Split(';')[0].Trim();

        return culture.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
    }
}
