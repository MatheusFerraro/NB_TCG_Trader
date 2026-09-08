using FluentValidation;
using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>Starts a password reset by emailing a single-use link (issue #69).</summary>
public sealed record ForgotPasswordRequest(string Email);

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

/// <summary>
/// Always answers 202 Accepted in the same shape and (near enough) the same time,
/// registered address or not: the response must never reveal whether an account
/// exists (CLAUDE.md §15, issue #69 AC).
/// </summary>
public sealed class ForgotPasswordHandler(
    UserManager<AppUser> users,
    AuthEmailNotifier notifier,
    ILogger<ForgotPasswordHandler> logger)
{
    public async Task<IResult> HandleAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);

        if (user is null)
        {
            // Logged without the address so the log is not itself an account oracle.
            logger.LogInformation("Password reset requested for an unknown address");
            return Results.Accepted();
        }

        // A locked-out account can still reset: the lockout guards password guessing,
        // and a proven mailbox is a stronger signal than the failed attempts that
        // tripped it. Unconfirmed addresses are allowed too — otherwise a user who
        // mistypes their password before confirming has no way back in.
        await notifier.SendPasswordResetAsync(user);

        return Results.Accepted();
    }
}
