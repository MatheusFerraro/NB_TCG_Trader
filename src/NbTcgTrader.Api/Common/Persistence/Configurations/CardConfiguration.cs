using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.Property(c => c.ExternalId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Number).HasMaxLength(50);
        builder.Property(c => c.Rarity).HasMaxLength(100);
        builder.Property(c => c.ImageUrl).HasMaxLength(2048);

        // Variable per-TCG attributes as Postgres jsonb.
        builder.Property(c => c.Metadata).HasColumnType("jsonb");

        builder.HasOne(c => c.Game)
            .WithMany(g => g.Cards)
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CardSet)
            .WithMany(s => s.Cards)
            .HasForeignKey(c => c.CardSetId)
            .OnDelete(DeleteBehavior.SetNull);

        // Lookups by display name within a game, and by provider id (CLAUDE.md §7).
        builder.HasIndex(c => new { c.GameId, c.Name });
        builder.HasIndex(c => c.ExternalId);
    }
}
