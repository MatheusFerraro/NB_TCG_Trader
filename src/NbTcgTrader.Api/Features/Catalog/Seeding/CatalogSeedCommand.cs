using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>
/// One-shot CLI command (BACKLOG #72): <c>dotnet run -- seed-catalog</c> ingests the
/// pokemon-tcg-data dataset into Postgres, then exits without serving requests. Runs the
/// migrations first so seeding works against a fresh database in any environment — the
/// seed is a deliberate operation, unlike the Development-only startup migration.
/// </summary>
public static class CatalogSeedCommand
{
    public const string CommandName = "seed-catalog";

    /// <summary>True when the process was launched to seed rather than serve.</summary>
    public static bool IsRequested(string[] args) => args.Contains(CommandName);

    /// <summary>Migrates, seeds, and returns a process exit code (0 = success).</summary>
    public static async Task<int> RunAsync(WebApplication app, CancellationToken cancellationToken)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var seeder = services.GetRequiredService<CatalogSeeder>();
        await seeder.SeedAsync(cancellationToken);

        return 0;
    }
}
