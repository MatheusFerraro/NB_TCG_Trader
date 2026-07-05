using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NbTcgTrader.Api.Common.Errors;

/// <summary>
/// Catches unhandled exceptions and returns a ProblemDetails response
/// (application/problem+json) without leaking exception messages or stack traces
/// to the client (CLAUDE.md §10, §15). The full exception is logged server-side.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Upstream dependency failures (card catalog down/slow) are expected
        // operational events, not bugs: log as a warning and answer 503 with the
        // exception's own client-safe title/detail so the UI can show a friendly
        // "try again" message instead of a generic 500.
        if (exception is UpstreamUnavailableException upstream)
        {
            logger.LogWarning(
                exception,
                "Upstream dependency unavailable processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = upstream.Title,
                    Detail = upstream.Detail,
                    Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.4",
                },
            });
        }

        logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Deliberately do NOT attach the Exception to the ProblemDetailsContext.
        // The exception is already logged above; leaving it off the context
        // guarantees no problem-details writer can serialize its message or stack
        // trace into the response, in any environment (CLAUDE.md §15).
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            },
        });
    }
}
