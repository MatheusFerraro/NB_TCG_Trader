using Microsoft.AspNetCore.Identity;

namespace NbTcgTrader.Api.Common.Domain;

/// <summary>
/// Application user, extending ASP.NET Core Identity with public-facing profile,
/// location, and contact fields (CLAUDE.md §7). Only the contact fields here are
/// ever exposed publicly — never the password hash or other identity internals.
/// </summary>
public sealed class AppUser : IdentityUser
{
    public required string DisplayName { get; set; }

    /// <summary>Home city, e.g. "Moncton" or "Ipaussu".</summary>
    public string? City { get; set; }

    public string? Country { get; set; }

    // Public contact channels surfaced on a listing. All optional.
    public string? ContactEmail { get; set; }

    public string? DiscordHandle { get; set; }

    public string? InstagramHandle { get; set; }

    public ICollection<CollectionItem> CollectionItems { get; set; } = new List<CollectionItem>();

    public ICollection<ImportJob> ImportJobs { get; set; } = new List<ImportJob>();
}
