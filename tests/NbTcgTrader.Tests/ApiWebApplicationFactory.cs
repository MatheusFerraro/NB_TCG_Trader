using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NbTcgTrader.Api.Common.Extensions;

namespace NbTcgTrader.Tests;

/// <summary>
/// Shared host factory for cross-cutting integration tests. The host boots in
/// Development (so the Development-only Scalar/CORS surface is exercised), but
/// these tests don't touch the database, so it disables startup migrations and
/// supplies a throwaway connection string — the DbContext is registered but
/// never opened, and the fail-fast connection-string guard is satisfied.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=nbtcg_test_unused;Username=test;Password=test");

        // The Development host also loads the developer's user-secrets; if those
        // set Admin:SeedEmails, the admin seed would query the (unreachable)
        // database above and kill the boot. Pin it empty so seeding no-ops.
        builder.UseSetting(AdminSeedExtensions.SeedEmailsKey, "");

        // Auth wiring validates the Jwt config on startup (ValidateOnStart), so the
        // host needs a valid signing key (>= 32 bytes) + issuer/audience to boot —
        // even for tests that never authenticate. A throwaway test key is fine here.
        builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
        builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
        builder.UseSetting("Jwt:Audience", TestJwt.Audience);

        // Catalog wiring validates the placeholder URL on startup (ValidateOnStart), so
        // the host needs a valid absolute URL to boot — even for tests that never hit the
        // catalog endpoint. A throwaway value is fine here.
        builder.UseSetting(
            "Catalog:PlaceholderImageUrl", "https://localhost/assets/card-placeholder.svg");
    }
}
