using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Update the authenticated user's public profile: display name, location, and
/// the public contact handles (CLAUDE.md §7, §15). Email/password are account
/// credentials and are intentionally not editable here. PUT semantics — every
/// optional field is replaced with the supplied value (null clears it).
/// </summary>
public sealed record UpdateProfileRequest(
    string DisplayName,
    string? City,
    string? Country,
    string? ContactEmail,
    string? DiscordHandle,
    string? InstagramHandle);

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        // Same shape as the profile fields on RegisterValidator so the rules stay
        // consistent across the two write paths.
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

public sealed class UpdateProfileHandler(UserManager<AppUser> users)
{
    public async Task<IResult> HandleAsync(UpdateProfileRequest request, ClaimsPrincipal principal)
    {
        // Identify the user from the validated JWT's `sub` claim — never from the
        // request body — so a caller can only ever edit their own profile
        // (CLAUDE.md §15). A valid token for a since-deleted user is unauthorized.
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        user.DisplayName = request.DisplayName;
        user.City = request.City;
        user.Country = request.Country;
        user.ContactEmail = request.ContactEmail;
        user.DiscordHandle = request.DiscordHandle;
        user.InstagramHandle = request.InstagramHandle;

        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(errors);
        }

        // The same shape /auth/me returns, so the client can refresh its view.
        return Results.Ok(UserResponse.From(user));
    }
}
