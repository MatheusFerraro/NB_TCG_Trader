using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Auth;

public sealed record RefreshRequest(string RefreshToken);

public sealed class RefreshValidator : AbstractValidator<RefreshRequest>
{
    public RefreshValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public sealed class RefreshHandler(
    AppDbContext db,
    UserManager<AppUser> users,
    ITokenService tokens,
    TokenIssuer issuer,
    ILogger<RefreshHandler> logger)
{
    private static IResult InvalidToken() =>
        Results.Problem(
            detail: "The refresh token is invalid or has expired.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed");

    public async Task<IResult> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var now = DateTimeOffset.UtcNow;

        var stored = await db.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Unknown or expired tokens are rejected with the same opaque error
        // (CLAUDE.md §15). A *revoked* token is handled specially below.
        if (stored is null)
        {
            return InvalidToken();
        }

        // Reuse detection: a token that was already rotated out (RevokedAt set) is
        // being replayed. A single-use token presented twice means either the
        // legitimate successor or a thief holds a copy — a compromise signal. Kill
        // the whole family so both sides lose the session, and log it for audit
        // (CLAUDE.md §15). No token value is logged, only the user id.
        if (stored.RevokedAt is not null)
        {
            var revokedFamily = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.RevokedAt, now),
                    cancellationToken);

            logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoked {RevokedCount} active token(s).",
                stored.UserId, revokedFamily);

            return InvalidToken();
        }

        // Live but past expiry: reject, same opaque error.
        if (!stored.IsActive(now))
        {
            return InvalidToken();
        }

        var user = await users.FindByIdAsync(stored.UserId);
        if (user is null)
        {
            return InvalidToken();
        }

        // A locked account must not mint fresh tokens (admin lock, credential
        // lockout). Same opaque rejection as any other invalid token.
        if (await users.IsLockedOutAsync(user))
        {
            return InvalidToken();
        }

        // Rotation must be single-use and all-or-nothing under concurrency
        // (CLAUDE.md §15). Wrap revoke + issue in one transaction so they commit
        // together or not at all.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Atomically revoke: the conditional WHERE means only the first of any
        // concurrent refreshes flips RevokedAt and sees revoked == 1. A loser sees
        // 0 and is rejected, so a token can never mint two successors.
        var revoked = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, now),
                cancellationToken);

        if (revoked == 0)
        {
            return InvalidToken();
        }

        // Refresh proves the account is still in use — stamp LastSeenAt for the
        // admin hub's activity view (login keeps LastLoginAt).
        user.LastSeenAt = now;
        await users.UpdateAsync(user);

        var response = await issuer.IssueAsync(user, cancellationToken);

        // Chain the revoked token to its successor for audit (CLAUDE.md §15).
        await db.RefreshTokens
            .Where(t => t.Id == stored.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    t => t.ReplacedByTokenHash, tokens.HashRefreshToken(response.RefreshToken)),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(response);
    }
}
