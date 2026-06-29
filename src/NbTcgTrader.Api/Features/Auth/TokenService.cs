using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>A signed JWT access token and the instant it expires.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// A freshly minted refresh token. <see cref="Value"/> is the opaque secret
/// returned to the client; only <see cref="Hash"/> is ever persisted.
/// </summary>
public sealed record RefreshTokenPair(string Value, string Hash, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints JWT access tokens and opaque refresh tokens (CLAUDE.md §15). Stateless —
/// persistence of refresh tokens is the caller's job (see <see cref="TokenIssuer"/>).
/// </summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user);

    RefreshTokenPair CreateRefreshToken();

    /// <summary>SHA-256 → Base64 of a raw refresh token, for storage and lookup.</summary>
    string HashRefreshToken(string rawToken);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // sub/jti are standard; email + name are convenience claims for the client.
        // No roles or secrets travel in the token.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials,
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }

    public RefreshTokenPair CreateRefreshToken()
    {
        // 256 bits of CSPRNG entropy, URL-safe so it survives transport untouched.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Base64UrlEncoder.Encode(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);

        return new RefreshTokenPair(value, HashRefreshToken(value), expiresAt);
    }

    public string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(hash);
    }
}
