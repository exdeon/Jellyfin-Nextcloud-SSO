using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.NextcloudOAuth2.Sso;

/// <summary>
/// Данные одного шага OAuth2-flow.
/// </summary>
internal sealed class StateEntry
{
    public DateTime ExpiresAtUtc { get; init; }

    public string DeviceId { get; init; } = string.Empty;

    public string ReturnUrl { get; init; } = SsoOptions.DefaultReturnUrl;

    public string? CodeVerifier { get; init; }
}

/// <summary>
/// Хранилище state (CSRF) + служебных данных flow и cookie-привязка к браузеру.
/// Jellyfin работает в одном процессе — достаточно in-memory.
/// </summary>
internal static class StateStore
{
    private static readonly Dictionary<string, StateEntry> Entries = new();

    /// <summary>
    /// Сохраняет state с ограничением размера и чисткой просроченных записей.
    /// </summary>
    internal static void Store(string state, StateEntry entry)
    {
        lock (SsoLock.Root)
        {
            var now = DateTime.UtcNow;

            // Чистим просроченные записи
            foreach (var expired in Entries.Where(kv => kv.Value.ExpiresAtUtc < now).Select(kv => kv.Key).ToList())
            {
                Entries.Remove(expired);
            }

            // Ограничиваем размер хранилища (защита от исчерпания памяти)
            if (Entries.Count >= SsoOptions.MaxStateEntries)
            {
                var oldest = Entries.OrderBy(kv => kv.Value.ExpiresAtUtc)
                    .Take(Entries.Count - SsoOptions.MaxStateEntries + 1)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in oldest)
                {
                    Entries.Remove(key);
                }
            }

            Entries[state] = entry;
        }
    }

    /// <summary>
    /// Забирает state: он используется только один раз.
    /// </summary>
    internal static StateEntry? Take(string state)
    {
        lock (SsoLock.Root)
        {
            if (!Entries.TryGetValue(state, out var entry))
            {
                return null;
            }

            Entries.Remove(state);
            return entry.ExpiresAtUtc > DateTime.UtcNow ? entry : null;
        }
    }

    /// <summary>
    /// Удаляет state (например, если шаг flow не удался).
    /// </summary>
    internal static void Remove(string state)
    {
        lock (SsoLock.Root)
        {
            Entries.Remove(state);
        }
    }

    /// <summary>
    /// Привязывает hash(state) к HttpOnly-cookie браузера (SameSite=Lax).
    /// </summary>
    internal static void SetBindingCookie(HttpContext context, string state)
    {
        context.Response.Cookies.Append(SsoOptions.StateCookieName, HashState(state), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = SsoOptions.StateLifetime
        });
    }

    /// <summary>
    /// Проверяет, что hash(state) совпадает с cookie браузера (защита от login CSRF).
    /// </summary>
    internal static bool VerifyBinding(HttpContext context, string state)
    {
        if (!context.Request.Cookies.TryGetValue(SsoOptions.StateCookieName, out var cookieValue) ||
            string.IsNullOrEmpty(cookieValue))
        {
            return false;
        }

        var expected = HashState(state);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(cookieValue),
            Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>
    /// Генерирует криптографически стойкий случайный state (URL-safe).
    /// </summary>
    internal static string NewState()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string HashState(string state)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();
    }
}
