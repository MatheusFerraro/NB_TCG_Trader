using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>Returns the authenticated user's public profile (CLAUDE.md §7, §15).</summary>
public sealed class MeHandler(UserManager<AppUser> users)
{
    public async Task<IResult> HandleAsync(ClaimsPrincipal principal)
    {
        // The id comes from the validated JWT's `sub` claim — never from the client
        // body. Inbound claim mapping is disabled, so read `sub` directly. If the
        // token is valid but the user is gone, treat it as unauthorized.
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

        return Results.Ok(UserResponse.From(user, await users.GetRolesAsync(user)));
    }
}
