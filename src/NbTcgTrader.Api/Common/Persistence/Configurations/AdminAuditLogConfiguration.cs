using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        // Identity user ids are GUID strings (36 chars); Identity caps them at 450.
        builder.Property(a => a.AdminUserId).IsRequired().HasMaxLength(450);
        builder.Property(a => a.TargetUserId).HasMaxLength(450);

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);

        // The audit page reads newest-first; the user detail page reads per target.
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.TargetUserId);

        // Deliberately no FK to AspNetUsers: audit rows must survive user deletion.
    }
}
