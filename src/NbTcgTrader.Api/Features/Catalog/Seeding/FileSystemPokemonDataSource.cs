using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>
/// Reads the pokemon-tcg-data dataset from a local checkout (BACKLOG #72). Sets live in
/// a single <c>sets/{lang}.json</c> array; each set's cards live in
/// <c>cards/{lang}/{setId}.json</c>. Missing card files are treated as an empty set
/// (some sets legitimately have no card file) rather than a hard failure, so one gap
/// never aborts the whole seed.
/// </summary>
public sealed class FileSystemPokemonDataSource(IOptions<SeedOptions> options)
    : ICardDataSource
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private readonly SeedOptions _options = options.Value;

    public async Task<IReadOnlyList<PokemonSetData>> LoadSetsAsync(
        CancellationToken cancellationToken)
    {
        var root = RequireDataPath();
        var setsFile = Path.Combine(root, "sets", $"{_options.Language}.json");

        if (!File.Exists(setsFile))
        {
            throw new FileNotFoundException(
                $"Sets file not found at '{setsFile}'. Point Seed:DataPath at a " +
                "pokemon-tcg-data checkout.", setsFile);
        }

        return await ReadJsonAsync<PokemonSetData>(setsFile, cancellationToken);
    }

    public async Task<IReadOnlyList<PokemonCardData>> LoadCardsAsync(
        string setId, CancellationToken cancellationToken)
    {
        var root = RequireDataPath();
        var cardsFile = Path.Combine(root, "cards", _options.Language, $"{setId}.json");

        return File.Exists(cardsFile)
            ? await ReadJsonAsync<PokemonCardData>(cardsFile, cancellationToken)
            : [];
    }

    private string RequireDataPath()
    {
        if (string.IsNullOrWhiteSpace(_options.DataPath))
        {
            throw new InvalidOperationException(
                "Seed:DataPath is not configured. Set it to a local pokemon-tcg-data checkout.");
        }

        if (!Directory.Exists(_options.DataPath))
        {
            throw new DirectoryNotFoundException(
                $"Seed:DataPath '{_options.DataPath}' does not exist.");
        }

        return _options.DataPath;
    }

    private static async Task<IReadOnlyList<T>> ReadJsonAsync<T>(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<T>>(
            stream, JsonOptions, cancellationToken);
        return items ?? [];
    }
}
