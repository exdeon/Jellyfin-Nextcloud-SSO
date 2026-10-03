using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.NextcloudOAuth2.Models;

/// <summary>
/// Ответ OIDC Discovery endpoint.
/// </summary>
public class OIDCDiscovery
{
    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; set; }

    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; set; }

    [JsonPropertyName("userinfo_endpoint")]
    public string? UserInfoEndpoint { get; set; }
}

/// <summary>
/// Ответ token endpoint.
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

/// <summary>
/// Информация о пользователе.
/// </summary>
public class UserInfo
{
    /// <summary>
    /// Стабильный (неизменяемый) идентификатор пользователя в Nextcloud.
    /// </summary>
    public string? UserId { get; set; }

    public string? PreferredUsername { get; set; }

    public string? Email { get; set; }

    public string? Name { get; set; }
}

/// <summary>
/// Обёртка ответа Nextcloud OCS API.
/// </summary>
public class NextcloudUserWrapper
{
    public OcsWrapper? Ocs { get; set; }
}

public class OcsWrapper
{
    public NextcloudUser? Data { get; set; }
}

public class NextcloudUser
{
    public string? Id { get; set; }

    public string? Email { get; set; }

    [JsonPropertyName("displayname")]
    public string? DisplayName { get; set; }
}
