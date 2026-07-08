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

        // Marketplace query filters on these flags (CLAUDE.md §7). The composite btree
        // still serves the browse COUNT (an index-only scan over the two flags).
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => new { i.IsForSale, i.IsPrivate });

        // Marketplace browse (#65): a partial index over only the public rows, in the
        // exact page order (CreatedAt DESC, Id DESC). It lets the paged fetch walk the
        // index and nested-loop-join just the page's rows — no full sort of every public
        // listing, no whole-Cards hash. On ~6k public rows this took the first page from
        // ~20 ms to ~0.6 ms; price/currency filters ride along as a residual filter on the
        // same index (see docs/perf-marketplace-browse.md). The (CreatedAt DESC, Id DESC)
        // shape is also the clean seam for a later keyset/cursor upgrade for deep pages.
        builder.HasIndex(i => new { i.CreatedAt, i.Id })
            .IsDescending(true, true)
            .HasFilter("\"IsForSale\" AND NOT \"IsPrivate\"")
            .HasDatabaseName("ix_collectionitems_public_browse");
    }
}
