using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Issues an access + refresh token pair for a user and persists the refresh
/// token's hash. Shared by register, login, and refresh inside the Auth slice so
/// the rotation rules live in exactly one place (CLAUDE.md §6, §15).
/// </summary>
public sealed class TokenIssuer(AppDbContext db, ITokenService tokens)
{
    public async Task<AuthResponse> IssueAsync(AppUser user, CancellationToken cancellationToken)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refresh.Hash,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = refresh.ExpiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            access.Value,
            access.ExpiresAt,
            refresh.Value,
            refresh.ExpiresAt,
            UserResponse.From(user));
    }
}
