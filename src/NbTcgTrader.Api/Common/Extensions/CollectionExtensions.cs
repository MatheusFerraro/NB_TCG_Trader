using NbTcgTrader.Api.Features.Collection;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Collection/binder slice wiring (BACKLOG #10). Validators are picked up by the
/// assembly-wide FluentValidation scan in <see cref="AuthenticationExtensions"/>.
/// </summary>
public static class CollectionExtensions
{
    public static IServiceCollection AddApiCollection(this IServiceCollection services)
    {
        services.AddScoped<AddCardHandler>();
        return services;
    }
}
