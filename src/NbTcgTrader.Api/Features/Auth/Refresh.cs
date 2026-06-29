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
    TokenIssuer issuer)
{
    private static IResult InvalidToken() =>
        Results.Problem(
            detail: "The refresh token is invalid or has expired.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed");

    public async Task<IResult> HandleAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);

        var stored = await db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Unknown, already-used (revoked), or expired tokens are all rejected the
        // same way (CLAUDE.md §15).
        if (stored is null || !stored.IsActive(DateTimeOffset.UtcNow))
        {
            return InvalidToken();
        }

        var user = await users.FindByIdAsync(stored.UserId);
        if (user is null)
        {
            return InvalidToken();
        }

        // Rotate: issue the successor first so we can chain the old token to it,
        // then revoke the old one. Both saves happen in IssueAsync / here.
        var response = await issuer.IssueAsync(user, cancellationToken);

        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.ReplacedByTokenHash = tokens.HashRefreshToken(response.RefreshToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(response);
    }
}
