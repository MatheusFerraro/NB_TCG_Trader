using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence;

/// <summary>
/// EF Core context owning the entire schema: ASP.NET Core Identity tables (via
/// <see cref="IdentityDbContext{TUser}"/>) plus the TCG-agnostic domain model
/// (CLAUDE.md §7). Entity shape lives in <c>Configurations/</c>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Game> Games => Set<Game>();

    public DbSet<CardSet> CardSets => Set<CardSet>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity tables first, then our per-entity configurations.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
