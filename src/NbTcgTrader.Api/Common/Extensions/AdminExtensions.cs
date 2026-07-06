using NbTcgTrader.Api.Features.Admin;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Admin hub slice wiring (admin/operations backlog). Validators are picked up by
/// the assembly-wide FluentValidation scan in <see cref="AuthenticationExtensions"/>.
/// <see cref="IHttpContextAccessor"/> feeds the audit writer's correlation ids.
/// </summary>
public static class AdminExtensions
{
    public static IServiceCollection AddApiAdmin(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<AdminAuditWriter>();
        services.AddScoped<GetDashboardHandler>();
        services.AddScoped<ListUsersHandler>();
        services.AddScoped<GetUserDetailHandler>();
        services.AddScoped<LockUserHandler>();
        services.AddScoped<UnlockUserHandler>();
        services.AddScoped<ListAuditLogHandler>();
        services.AddScoped<GetActivityHandler>();
        return services;
    }
}
