using FluentValidation;
using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Email;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Confirms an address from the link in the verification email (issue #69). The
/// token is base64url-encoded by <see cref="EmailLinkBuilder"/> so it survives the
/// query string.
/// </summary>
public sealed record VerifyEmailRequest(string UserId, string Token);

public sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(450);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(4096);
    }
}

public sealed class VerifyEmailHandler(
    UserManager<AppUser> users,
    ILogger<VerifyEmailHandler> logger)
{
    // One message for every failure mode. A distinct "no such user" response would
    // turn this endpoint into a user-id oracle (CLAUDE.md §15).
    private static IResult InvalidLink() =>
        Results.Problem(
            detail: "This confirmation link is invalid or has expired. Request a new one.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Confirmation failed");

    public async Task<IResult> HandleAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var token = EmailLinkBuilder.TryDecodeToken(request.Token);
        if (token is null)
        {
            return InvalidLink();
        }

        var user = await users.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return InvalidLink();
        }

        // Already confirmed: report success. Clicking the link twice (a mail client
        // prefetch, a second tab) is not an error the user can act on.
        if (user.EmailConfirmed)
        {
            return Results.NoContent();
        }

        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            // Codes only — the token itself never reaches the log.
            logger.LogInformation(
                "Email confirmation failed for user {UserId}: {Codes}",
                user.Id,
                string.Join(",", result.Errors.Select(e => e.Code)));

            return InvalidLink();
        }

        logger.LogInformation("Email confirmed for user {UserId}", user.Id);
        return Results.NoContent();
    }
}
