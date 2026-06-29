using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.Property(j => j.FileName).IsRequired().HasMaxLength(260);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(j => j.User)
            .WithMany(u => u.ImportJobs)
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(j => j.UserId);
    }
}
