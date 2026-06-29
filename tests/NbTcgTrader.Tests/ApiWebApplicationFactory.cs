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
    }
}
