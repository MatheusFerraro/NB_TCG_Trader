using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NbTcgTrader.Api.Common.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the context without booting
/// the web host. Migration generation needs a provider + connection string but no
/// live database. Resolves <c>ConnectionStrings:Default</c> from user-secrets,
/// then environment variables (env wins), falling back to the local dev defaults
/// already published in <c>docker-compose.yml</c>/<c>.env.example</c> (non-secret).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=nbtcg;Username=nbtcg;Password=nbtcg_dev_password";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("Default")
            ?? LocalDevConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
