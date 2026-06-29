using FluentValidation;

namespace NbTcgTrader.Api.Common.Filters;

/// <summary>
/// Endpoint filter that runs the FluentValidation <see cref="IValidator{T}"/> for
/// the request argument of type <typeparamref name="T"/> before the handler. On
/// failure it short-circuits with a 400 ValidationProblem (ProblemDetails with a
/// field→errors map), per CLAUDE.md §10. If no validator is registered the request
/// passes through untouched.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    private readonly IValidator<T>? _validator;

    public ValidationFilter(IValidator<T>? validator = null) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (_validator is not null)
        {
            var argument = context.Arguments.OfType<T>().FirstOrDefault();
            if (argument is not null)
            {
                var result = await _validator.ValidateAsync(
                    argument, context.HttpContext.RequestAborted);

                if (!result.IsValid)
                {
                    return Results.ValidationProblem(result.ToDictionary());
                }
            }
        }

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Attaches <see cref="ValidationFilter{T}"/> for the request type <typeparamref name="T"/>.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class =>
        builder
            .AddEndpointFilter<ValidationFilter<T>>()
            .ProducesValidationProblem();
}
