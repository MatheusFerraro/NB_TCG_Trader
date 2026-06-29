using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.Property(r => r.RawName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.RawSet).HasMaxLength(200);
        builder.Property(r => r.RawNumber).HasMaxLength(50);
        builder.Property(r => r.Price).HasPrecision(18, 2);
        builder.Property(r => r.Condition).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.MatchStatus).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(r => r.ImportJob)
            .WithMany(j => j.Rows)
            .HasForeignKey(r => r.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep the catalog match optional; never cascade-delete a row's matched card.
        builder.HasOne(r => r.MatchedCard)
            .WithMany()
            .HasForeignKey(r => r.MatchedCardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ImportJobId);
    }
}
