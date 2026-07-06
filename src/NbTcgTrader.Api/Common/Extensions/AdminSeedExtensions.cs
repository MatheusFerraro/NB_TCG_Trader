using Microsoft.AspNetCore.Identity;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Safe first-admin bootstrap (admin hub). Reads <c>Admin:SeedEmails</c> (env:
/// <c>Admin__SeedEmails</c>, comma/semicolon-separated) and grants the Admin role
/// to those accounts if they exist. Idempotent, so it runs on every startup: an
/// operator sets the variable, restarts, and the promotion happens without manual
/// SQL. Accounts must already be registered — seeding never creates users or
/// passwords. When the variable is unset, nothing runs and the database is never
/// touched (keeps DB-less test hosts bootable).
/// </summary>
public static class AdminSeedExtensions
{
    public const string SeedEmailsKey = "Admin:SeedEmails";

    public static async Task SeedAdminsAsync(this WebApplication app)
    {
        var configured = app.Configuration[SeedEmailsKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var emails = configured
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var scope = app.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AdminSeed");

        if (!await roles.RoleExistsAsync(AuthenticationExtensions.AdminRole))
        {
            var createRoleResult = await roles.CreateAsync(
                new IdentityRole(AuthenticationExtensions.AdminRole));
            if (!createRoleResult.Succeeded)
            {
                logger.LogError(
                    "Admin seed: failed to create Admin role: {Errors}",
                    string.Join(
                        "; ",
                        createRoleResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
                return;
            }
        }

        foreach (var email in emails)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                // Config values are operator-supplied, not user data — safe to log.
                logger.LogWarning(
                    "Admin seed: no account registered for configured email {Email}", email);
                continue;
            }

            if (!await users.IsInRoleAsync(user, AuthenticationExtensions.AdminRole))
            {
                var addToRoleResult = await users.AddToRoleAsync(
                    user, AuthenticationExtensions.AdminRole);
                if (!addToRoleResult.Succeeded)
                {
                    logger.LogError(
                        "Admin seed: failed to grant Admin role to user {UserId}: {Errors}",
                        user.Id,
                        string.Join(
                            "; ",
                            addToRoleResult.Errors.Select(e => $"{e.Code}: {e.Description}")));
                    continue;
                }

                logger.LogInformation("Admin seed: granted Admin role to user {UserId}", user.Id);
            }
        }
    }
}
