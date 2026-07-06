using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Filters;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Maps the admin hub endpoints (admin/operations backlog). The whole group
/// requires the Admin role policy — authorization is enforced here and again
/// implicitly in handlers that scope queries by the target id, never by trusting
/// the client (CLAUDE.md §15). Non-admins get 403, anonymous callers 401.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin")
            .WithTags("Admin")
            .RequireAuthorization(AuthenticationExtensions.AdminPolicy);

        group.MapGet("/dashboard",
                (GetDashboardHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(ct))
            .WithName("AdminDashboard")
            .Produces<AdminDashboardResponse>();

        group.MapGet("/users",
                ([AsParameters] ListUsersRequest request,
                        ListUsersHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("AdminListUsers")
            .WithValidation<ListUsersRequest>()
            .Produces<AdminUserListResponse>();

        group.MapGet("/users/{userId}",
                (string userId, ClaimsPrincipal principal,
                        GetUserDetailHandler handler, CancellationToken ct) =>
                    WithAdminId(principal, adminId => handler.HandleAsync(userId, adminId, ct)))
            .WithName("AdminGetUserDetail")
            .Produces<AdminUserDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/users/{userId}/lock",
                (string userId, LockUserRequest request, ClaimsPrincipal principal,
                        LockUserHandler handler, CancellationToken ct) =>
                    WithAdminId(principal,
                        adminId => handler.HandleAsync(userId, adminId, request, ct)))
            .WithName("AdminLockUser")
            .WithValidation<LockUserRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/users/{userId}/unlock",
                (string userId, UnlockUserRequest request, ClaimsPrincipal principal,
                        UnlockUserHandler handler, CancellationToken ct) =>
                    WithAdminId(principal,
                        adminId => handler.HandleAsync(userId, adminId, request, ct)))
            .WithName("AdminUnlockUser")
            .WithValidation<UnlockUserRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/audit-log",
                ([AsParameters] ListAuditLogRequest request,
                        ListAuditLogHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(request, ct))
            .WithName("AdminListAuditLog")
            .WithValidation<ListAuditLogRequest>()
            .Produces<AdminAuditLogResponse>();

        group.MapGet("/activity",
                (GetActivityHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(ct))
            .WithName("AdminGetActivity")
            .Produces<AdminActivityResponse>();

        return endpoints;
    }

    // The acting admin's id comes from the validated JWT's `sub` claim — never
    // from the request — so audit rows always name the real actor (CLAUDE.md §15).
    private static Task<IResult> WithAdminId(
        ClaimsPrincipal principal,
        Func<string, Task<IResult>> action)
    {
        var adminId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return adminId is null
            ? Task.FromResult(Results.Unauthorized())
            : action(adminId);
    }
}
