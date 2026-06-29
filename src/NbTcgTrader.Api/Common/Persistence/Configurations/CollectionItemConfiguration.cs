using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class CollectionItemConfiguration : IEntityTypeConfiguration<CollectionItem>
{
    public void Configure(EntityTypeBuilder<CollectionItem> builder)
    {
        // Enums stored as readable strings rather than opaque ints.
        builder.Property(i => i.Condition).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.Currency).HasConversion<string>().HasMaxLength(10);

        builder.Property(i => i.Price).HasPrecision(18, 2);
        builder.Property(i => i.Notes).HasMaxLength(1000);

        builder.HasOne(i => i.User)
            .WithMany(u => u.CollectionItems)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Don't delete a catalog card out from under existing binder rows/listings.
        builder.HasOne(i => i.Card)
            .WithMany(c => c.CollectionItems)
            .HasForeignKey(i => i.CardId)
            .OnDelete(DeleteBehavior.Restrict);

        // Marketplace query filters on these flags (CLAUDE.md §7).
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => new { i.IsForSale, i.IsPrivate });
    }
}
