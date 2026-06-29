using Microsoft.EntityFrameworkCore;
using Npgsql;
using NbTcgTrader.Api.Common.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// High-fidelity check: apply the InitialCreate migration against a real Postgres
// (via Testcontainers) and confirm the schema lands. Requires Docker; when it's
// unavailable the test skips cleanly rather than failing the suite.
public sealed class DatabaseMigrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string? _dockerUnavailableReason;

    public async Task InitializeAsync()
    {
        try
        {
            // Building the container probes the Docker endpoint, so construct and
            // start inside the guard: no Docker engine reachable (e.g. CI without
            // Docker) means skip, not fail.
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _postgres = null;
            _dockerUnavailableReason = $"Docker is not available: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Migration_creates_identity_and_domain_tables()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var connectionString = _postgres!.GetConnectionString();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using (var context = new AppDbContext(options))
        {
            await context.Database.MigrateAsync();
        }

        var tables = await GetPublicTableNamesAsync(connectionString);

        // Identity + the full §7 domain model.
        tables.ShouldContain("AspNetUsers");
        tables.ShouldContain("AspNetRoles");
        tables.ShouldContain("Games");
        tables.ShouldContain("CardSets");
        tables.ShouldContain("Cards");
        tables.ShouldContain("CollectionItems");
        tables.ShouldContain("ImportJobs");
        tables.ShouldContain("ImportRows");
        tables.ShouldContain("RefreshTokens");
    }

    private static async Task<List<string>> GetPublicTableNamesAsync(string connectionString)
    {
        var names = new List<string>();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
