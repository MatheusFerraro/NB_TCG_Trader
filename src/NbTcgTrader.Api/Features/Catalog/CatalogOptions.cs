namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Non-secret catalog presentation options. <see cref="PlaceholderImageUrl"/> is the
/// absolute URL substituted at the response-DTO layer when the provider returns a card
/// without an image (CLAUDE.md §8 spirit: never drop a card). It differs per environment
/// (localhost vs Azure) and is consumed by a separate frontend origin, so it must be an
/// absolute URL read from config — never hard-coded or derived from the request. The
/// domain <c>Card.ImageUrl</c> stays nullable; the placeholder lives only in responses.
/// </summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public string PlaceholderImageUrl { get; init; } = string.Empty;
}
