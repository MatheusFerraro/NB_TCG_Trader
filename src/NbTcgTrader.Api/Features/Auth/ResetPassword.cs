using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Email;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>Completes a password reset using the token from the emailed link (issue #69).</summary>
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(4096);

        // Mirrors the Identity policy (see PersistenceExtensions) so the client gets
        // field errors before the store is touched; Identity still enforces it.
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12).MaximumLength(256);
    }
}

public sealed class ResetPasswordHandler(
    UserManager<AppUser> users,
    AppDbContext db,
    AuthEmailNotifier notifier,
    ILogger<ResetPasswordHandler> logger)
{
    /// <summary>
    /// One opaque failure for a bad address and a bad token alike — a distinct
    /// "unknown email" would undo the non-enumerating forgot-password endpoint.
    /// </summary>
    private static IResult InvalidLink() =>
        Results.Problem(
            detail: "This reset link is invalid or has expired. Request a new one.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Password reset failed");

    public async Task<IResult> HandleAsync(
        ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var token = EmailLinkBuilder.TryDecodeToken(request.Token);
        if (token is null)
        {
            return InvalidLink();
        }

        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidLink();
        }

        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            // A rejected *password* is actionable ("too short", "needs a digit") and
            // says nothing about whether the account exists — only an attacker holding
            // a valid token could ever see it. A rejected *token* stays opaque.
            var passwordErrors = result.Errors
                .Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
                .ToArray();

            if (passwordErrors.Length > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(ResetPasswordRequest.NewPassword)] =
                        passwordErrors.Select(e => e.Description).ToArray(),
                });
            }

            logger.LogInformation(
                "Password reset rejected for user {UserId}: {Codes}",
                user.Id,
                string.Join(",", result.Errors.Select(e => e.Code)));

            return InvalidLink();
        }

        // The reset proves control of the mailbox, not of the old sessions. Anyone
        // holding a stolen refresh token keeps it until it expires unless we revoke
        // here, which would defeat the point of resetting (CLAUDE.md §15).
        var revoked = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        // The lockout that a forgotten password usually produces should not survive
        // the reset — the user has just proven the mailbox is theirs.
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);

        logger.LogInformation(
            "Password reset for user {UserId}; revoked {RevokedCount} refresh token(s)",
            user.Id,
            revoked);

        // Out-of-band notice: if the reset wasn't the account owner, this is how they
        // find out (issue #69 — "password reset confirmation or security notice").
        notifier.SendPasswordChangedNotice(user);

        return Results.NoContent();
    }
}
