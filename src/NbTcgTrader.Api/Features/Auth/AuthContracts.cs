using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Token bundle returned by register/login/refresh. The refresh token is opaque;
/// the access token is a JWT. Never includes the password hash or identity internals.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserResponse User);

/// <summary>Public projection of <see cref="AppUser"/> — only safe-to-expose fields (CLAUDE.md §15).</summary>
public sealed record UserResponse(
    string Id,
    string Email,
    string DisplayName,
    string? City,
    string? Country,
    string? ContactEmail,
    string? DiscordHandle,
    string? InstagramHandle)
{
    public static UserResponse From(AppUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.DisplayName,
        user.City,
        user.Country,
        user.ContactEmail,
        user.DiscordHandle,
        user.InstagramHandle);
}
