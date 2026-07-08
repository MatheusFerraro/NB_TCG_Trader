namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>
/// Settings for the one-shot catalog seeder (BACKLOG #72), bound from the <c>Seed</c>
/// configuration section. <see cref="DataPath"/> points at a local checkout of the
/// pokemon-tcg-data repository (deterministic and offline); the seeder reads
/// <c>{DataPath}/sets/{Language}.json</c> and <c>{DataPath}/cards/{Language}/{setId}.json</c>.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Filesystem path to the root of a pokemon-tcg-data checkout.</summary>
    public string? DataPath { get; init; }

    /// <summary>Dataset language folder to ingest (pokemon-tcg-data ships many).</summary>
    public string Language { get; init; } = "en";
}
