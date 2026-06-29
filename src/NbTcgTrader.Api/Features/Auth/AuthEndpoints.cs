using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Filters;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Maps the Auth slice endpoints: register, login, refresh, me (CLAUDE.md §6,
/// BACKLOG #6). The whole group is rate-limited with the stricter auth policy to
/// blunt brute-force / credential-stuffing (CLAUDE.md §10, §15).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        group.MapPost("/register",
                (RegisterRequest request, RegisterHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("Register")
            .WithValidation<RegisterRequest>()
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .AllowAnonymous();

        group.MapPost("/login",
                (LoginRequest request, LoginHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("Login")
            .WithValidation<LoginRequest>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        group.MapPost("/refresh",
                (RefreshRequest request, RefreshHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("Refresh")
            .WithValidation<RefreshRequest>()
            .Produces<AuthResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        group.MapGet("/me",
                (System.Security.Claims.ClaimsPrincipal user, MeHandler handler) =>
                    handler.HandleAsync(user))
            .WithName("Me")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return endpoints;
    }
}
