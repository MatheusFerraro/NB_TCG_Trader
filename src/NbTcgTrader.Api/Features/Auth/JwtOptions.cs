namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// JWT settings bound from the <c>Jwt</c> configuration section (CLAUDE.md §15).
/// <see cref="SigningKey"/> is a secret and is never committed — it comes from
/// user-secrets locally and environment variables in deployed environments. The
/// values are validated on startup (see <c>AuthenticationExtensions</c>).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Symmetric signing key; must be at least 256 bits (32 bytes).</summary>
    public string SigningKey { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>Access-token lifetime in minutes (~15 per CLAUDE.md §15).</summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; init; } = 7;
}
