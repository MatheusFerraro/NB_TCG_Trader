using System.Net;

namespace NbTcgTrader.Tests;

/// <summary>
/// A minimal test double for <see cref="HttpMessageHandler"/>. The project uses no
/// mocking library (it prefers Testcontainers for real dependencies), so this hand-
/// rolled handler lets HttpClient-based clients be unit-tested without a network call.
/// It records every request and returns a canned response produced by a factory.
/// </summary>
public sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    // Guarded by a lock: the catalog client's background prefetch sends requests from
    // a thread-pool thread while the test thread polls the properties below.
    private readonly List<HttpRequestMessage> _requests = [];

    /// <summary>Every request the handler has seen, in order (snapshot).</summary>
    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get { lock (_requests) { return _requests.ToArray(); } }
    }

    /// <summary>How many requests reached the handler (e.g. to prove caching).</summary>
    public int CallCount
    {
        get { lock (_requests) { return _requests.Count; } }
    }

    /// <summary>The URI of the most recent request, or <c>null</c> if none.</summary>
    public Uri? LastRequestUri
    {
        get { lock (_requests) { return _requests.Count == 0 ? null : _requests[^1].RequestUri; } }
    }

    /// <summary>Convenience factory: always return <paramref name="json"/> with the given status.</summary>
    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(responder(request));
    }
}
