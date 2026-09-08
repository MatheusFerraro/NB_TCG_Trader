using FluentValidation;
using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>Requests a fresh verification email for an address (issue #69).</summary>
public sealed record ResendVerificationRequest(string Email);

public sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationValidator() =>
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

/// <summary>
/// Always answers 202 Accepted, whether or not the address belongs to an account and
/// whether or not it is already confirmed. Anything else would let an anonymous
/// caller enumerate registered users (CLAUDE.md §15).
/// </summary>
public sealed class ResendVerificationHandler(
    UserManager<AppUser> users,
    AuthEmailNotifier notifier)
{
    public async Task<IResult> HandleAsync(
        ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);

        if (user is not null && !user.EmailConfirmed)
        {
            await notifier.SendVerificationAsync(user);
        }

        return Results.Accepted();
    }
}
