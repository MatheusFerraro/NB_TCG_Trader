namespace NbTcgTrader.Api.Common.Errors;

/// <summary>
/// Thrown when an upstream dependency (e.g. the external card catalog) fails or times
/// out. <see cref="GlobalExceptionHandler"/> maps it to a 503 ProblemDetails using
/// <see cref="Title"/>/<see cref="Detail"/>, which are OUR OWN client-safe strings —
/// never provider exception text — so nothing internal leaks (CLAUDE.md §15).
/// </summary>
public sealed class UpstreamUnavailableException(
    string title,
    string detail,
    Exception? innerException = null) : Exception(detail, innerException)
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;
}
