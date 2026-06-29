using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using Shouldly;

namespace NbTcgTrader.Tests;

// Offline checks on the EF model — built via the design-time factory, no database
// required. Guards the AC (AppUser contact/location fields, full domain present)
// and a few mapping choices that downstream slices rely on.
public class AppDbContextModelTests
{
    private static AppDbContext CreateContext() =>
        new AppDbContextFactory().CreateDbContext([]);

    [Theory]
    [InlineData(nameof(AppUser.DisplayName))]
    [InlineData(nameof(AppUser.City))]
    [InlineData(nameof(AppUser.Country))]
    [InlineData(nameof(AppUser.ContactEmail))]
    [InlineData(nameof(AppUser.DiscordHandle))]
    [InlineData(nameof(AppUser.InstagramHandle))]
    public void AppUser_carries_contact_and_location_fields(string propertyName)
    {
        using var context = CreateContext();

        var appUser = context.Model.FindEntityType(typeof(AppUser));

        appUser.ShouldNotBeNull();
        appUser.FindProperty(propertyName).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(typeof(Game))]
    [InlineData(typeof(CardSet))]
    [InlineData(typeof(Card))]
    [InlineData(typeof(CollectionItem))]
    [InlineData(typeof(ImportJob))]
    [InlineData(typeof(ImportRow))]
    public void Domain_entities_are_mapped(Type entityType)
    {
        using var context = CreateContext();

        context.Model.FindEntityType(entityType).ShouldNotBeNull();
    }

    [Fact]
    public void Card_is_indexed_on_game_name_and_external_id()
    {
        using var context = CreateContext();

        var card = context.Model.FindEntityType(typeof(Card));
        card.ShouldNotBeNull();

        var indexedColumnSets = card.GetIndexes()
            .Select(i => i.Properties.Select(p => p.Name).ToArray())
            .ToList();

        indexedColumnSets.ShouldContain(
            cols => cols.Length == 2 && cols[0] == nameof(Card.GameId) && cols[1] == nameof(Card.Name));
        indexedColumnSets.ShouldContain(
            cols => cols.Length == 1 && cols[0] == nameof(Card.ExternalId));
    }

    [Fact]
    public void Card_metadata_is_stored_as_jsonb()
    {
        using var context = CreateContext();

        var metadata = context.Model
            .FindEntityType(typeof(Card))!
            .FindProperty(nameof(Card.Metadata));

        metadata.ShouldNotBeNull();
        metadata.GetColumnType().ShouldBe("jsonb");
    }

    [Fact]
    public void Enum_properties_are_persisted_as_strings()
    {
        using var context = CreateContext();

        var condition = context.Model
            .FindEntityType(typeof(CollectionItem))!
            .FindProperty(nameof(CollectionItem.Condition));

        condition.ShouldNotBeNull();
        // A value converter to string means the provider type is string, not int.
        condition.GetProviderClrType().ShouldBe(typeof(string));
    }
}
