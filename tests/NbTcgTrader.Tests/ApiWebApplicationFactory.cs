using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

        // Auth wiring validates the Jwt config on startup (ValidateOnStart), so the
        // host needs a valid signing key (>= 32 bytes) + issuer/audience to boot —
        // even for tests that never authenticate. A throwaway test key is fine here.
        builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
        builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
        builder.UseSetting("Jwt:Audience", TestJwt.Audience);
    }
}
