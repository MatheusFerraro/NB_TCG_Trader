using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class CardSetConfiguration : IEntityTypeConfiguration<CardSet>
{
    public void Configure(EntityTypeBuilder<CardSet> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Code).IsRequired().HasMaxLength(50);
        builder.Property(s => s.ExternalId).HasMaxLength(100);

        builder.HasOne(s => s.Game)
            .WithMany(g => g.CardSets)
            .HasForeignKey(s => s.GameId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.ExternalId);
        builder.HasIndex(s => new { s.GameId, s.Code });
    }
}
