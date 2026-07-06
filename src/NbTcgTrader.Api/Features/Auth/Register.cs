using FluentValidation;
using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Sign-up request. Email + password create the account; the remaining fields
/// seed the public profile (CLAUDE.md §7) and are all optional.
/// </summary>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    string? City,
    string? Country,
    string? ContactEmail,
    string? DiscordHandle,
    string? InstagramHandle);

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);

        // Mirror the Identity password policy so the client gets a 400 with field
        // errors before we hit the store; Identity still enforces it server-side.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(256);

        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.ContactEmail).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactEmail).MaximumLength(256);
        RuleFor(x => x.DiscordHandle).MaximumLength(100);
        RuleFor(x => x.InstagramHandle).MaximumLength(100);
    }
}

public sealed class RegisterHandler(UserManager<AppUser> users, TokenIssuer issuer)
{
    public async Task<IResult> HandleAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            City = request.City,
            Country = request.Country,
            ContactEmail = request.ContactEmail,
            DiscordHandle = request.DiscordHandle,
            InstagramHandle = request.InstagramHandle,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Identity hashes the password (CLAUDE.md §15) and enforces uniqueness/policy.
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Surface Identity's failures as a 400 ValidationProblem, grouped by code
            // (e.g. DuplicateUserName, PasswordTooShort) so the client can react.
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(errors);
        }

        var response = await issuer.IssueAsync(user, cancellationToken);

        // The created user is retrievable at /auth/me with the returned access token;
        // there is no public /auth/users/{id} route to point at.
        return Results.Created("/auth/me", response);
    }
}
