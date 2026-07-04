using NbTcgTrader.Api.Features.Marketplace;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Marketplace slice wiring (BACKLOG #17). Validators are picked up by the assembly-wide
/// FluentValidation scan in <see cref="AuthenticationExtensions"/>.
/// </summary>
public static class MarketplaceExtensions
{
    public static IServiceCollection AddApiMarketplace(this IServiceCollection services)
    {
        services.AddScoped<BrowseListingsHandler>();
        return services;
    }
}
