using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Auth;

public sealed record LogoutRequest(string RefreshToken);

public sealed class LogoutValidator : AbstractValidator<LogoutRequest>
{
    public LogoutValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

/// <summary>
/// Revokes the caller's current refresh token on sign-out so it cannot outlive the
/// session server-side (CLAUDE.md §15). Access tokens are short-lived and cannot be
/// revoked individually; they lapse on their own within minutes.
/// </summary>
public sealed class LogoutHandler(AppDbContext db, ITokenService tokens)
{
    public async Task<IResult> HandleAsync(
        LogoutRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        // The owner comes from the validated JWT's `sub`, never the client body.
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);

        // Scope the revoke to the caller's own active token: a valid access token
        // can only end its own session, never revoke a token it doesn't own. The
        // result is idempotent and never reveals whether the token existed — logout
        // always reports success (CLAUDE.md §15).
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        return Results.NoContent();
    }
}
