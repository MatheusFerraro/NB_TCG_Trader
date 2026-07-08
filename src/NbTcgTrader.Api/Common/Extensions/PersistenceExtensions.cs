using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using Npgsql;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// EF Core (Npgsql/Postgres) and ASP.NET Core Identity wiring (CLAUDE.md §7, §15).
/// JWT bearer authentication and the auth endpoints land in a later slice (#6);
/// this only sets up the data layer and the identity stores/password policy.
/// </summary>
public static class PersistenceExtensions
{
    public static IServiceCollection AddApiPersistence(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            // Resolve the connection string from the fully-built configuration when
            // the context is first created. It comes from an environment variable /
            // user-secrets, never source (CLAUDE.md §10, §13). Fail fast with an
            // actionable message rather than a vague provider error later.
            var connectionString = serviceProvider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("Default");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'ConnectionStrings:Default' is not configured.");
            }

            options.UseNpgsql(WithExtensionsOnSearchPath(connectionString));
        });

        // Identity's token providers (password reset, email confirm, etc.) depend
        // on Data Protection. AddIdentityCore doesn't register it, so do it here.
        // NOTE: keys persist to the local store by default — deployment (#24) should
        // configure durable key storage so tokens survive container restarts.
        services.AddDataProtection();

        services
            .AddIdentityCore<AppUser>(options =>
            {
                // Password policy (CLAUDE.md §15). Lockout guards credential stuffing.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.User.RequireUniqueEmail = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    /// <summary>
    /// Ensures the dedicated <c>extensions</c> schema is on the connection's search_path.
    /// pg_trgm lives there (not in <c>public</c>, a Supabase linter hardening — see
    /// <c>MovePgTrgmToExtensionsSchema</c>), so unqualified extension functions such as
    /// <c>similarity()</c> and the <c>%</c> operator used for relevance ranking (#66) would
    /// otherwise fail to resolve. This mirrors Supabase's own default search_path; Postgres
    /// ignores a not-yet-created schema, and application tables still resolve to
    /// <c>public</c> first, so it is safe on a fresh database and existing deployments alike.
    /// A caller-supplied search_path is respected.
    /// </summary>
    private static string WithExtensionsOnSearchPath(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.SearchPath))
        {
            builder.SearchPath = "public, extensions";
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Applies pending migrations. Intended for Development startup only; deployed
    /// environments migrate as a deliberate, separate step (CLAUDE.md §13, #23).
    /// </summary>
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
