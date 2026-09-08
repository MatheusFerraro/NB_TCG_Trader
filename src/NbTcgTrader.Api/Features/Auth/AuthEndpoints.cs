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

        group.MapPost("/logout",
                (LogoutRequest request, System.Security.Claims.ClaimsPrincipal user,
                        LogoutHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, user, ct))
            .WithName("Logout")
            .WithValidation<LogoutRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        // Email-backed verification and account recovery (#69). All anonymous — a
        // user who cannot sign in is exactly who needs them — and rate-limited harder
        // than the rest of the group: each call can put a message in someone's inbox
        // and spend the provider's free quota.
        group.MapPost("/email/verify",
                (VerifyEmailRequest request, VerifyEmailHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("VerifyEmail")
            .WithValidation<VerifyEmailRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapPost("/email/verify/resend",
                (ResendVerificationRequest request, ResendVerificationHandler handler,
                        CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("ResendVerificationEmail")
            .WithValidation<ResendVerificationRequest>()
            .Produces(StatusCodes.Status202Accepted)
            .RequireRateLimiting(RateLimitingExtensions.EmailPolicy)
            .AllowAnonymous();

        group.MapPost("/password/forgot",
                (ForgotPasswordRequest request, ForgotPasswordHandler handler,
                        CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("ForgotPassword")
            .WithValidation<ForgotPasswordRequest>()
            .Produces(StatusCodes.Status202Accepted)
            .RequireRateLimiting(RateLimitingExtensions.EmailPolicy)
            .AllowAnonymous();

        group.MapPost("/password/reset",
                (ResetPasswordRequest request, ResetPasswordHandler handler,
                        CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("ResetPassword")
            .WithValidation<ResetPasswordRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapGet("/me",
                (System.Security.Claims.ClaimsPrincipal user, MeHandler handler) =>
                    handler.HandleAsync(user))
            .WithName("Me")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        group.MapPut("/me",
                (UpdateProfileRequest request, System.Security.Claims.ClaimsPrincipal user,
                        UpdateProfileHandler handler) =>
                    handler.HandleAsync(request, user))
            .WithName("UpdateProfile")
            .WithValidation<UpdateProfileRequest>()
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return endpoints;
    }
}
