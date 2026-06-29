using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Common.Persistence.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(g => g.Slug).IsUnique();
    }
}
