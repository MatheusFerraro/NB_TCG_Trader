using FluentValidation;
using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(UserManager<AppUser> users, TokenIssuer issuer)
{
    // One opaque message for every credential failure so the endpoint never reveals
    // whether an email exists (CLAUDE.md §15 — no user enumeration).
    private static IResult InvalidCredentials() =>
        Results.Problem(
            detail: "Invalid email or password.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed");

    public async Task<IResult> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidCredentials();
        }

        // Honour the lockout policy before checking the password.
        if (await users.IsLockedOutAsync(user))
        {
            return InvalidCredentials();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            // Count the failure so repeated misses trip the lockout (CLAUDE.md §15).
            await users.AccessFailedAsync(user);
            return InvalidCredentials();
        }

        await users.ResetAccessFailedCountAsync(user);

        // Activity stamps for the admin hub's operational views. Coarse on purpose —
        // no per-request tracking, no IPs (privacy-conscious by design).
        var now = DateTimeOffset.UtcNow;
        user.LastLoginAt = now;
        user.LastSeenAt = now;
        await users.UpdateAsync(user);

        var response = await issuer.IssueAsync(user, cancellationToken);
        return Results.Ok(response);
    }
}
