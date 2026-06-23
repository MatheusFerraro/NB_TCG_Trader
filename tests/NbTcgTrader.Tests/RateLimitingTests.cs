using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace NbTcgTrader.Tests;

// Separate class => its own WebApplicationFactory => its own DI container and a
// fresh global rate-limiter bucket, isolated from the other integration tests.
[Collection(IntegrationTestCollection.Name)]
public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Global_limiter_returns_429_once_the_per_window_limit_is_exceeded()
    {
        var client = _factory.CreateClient();

        // The global limiter permits 100 requests/minute per client IP (constant
        // for the in-memory test server). The 100th still succeeds...
        HttpResponseMessage? lastWithinLimit = null;
        for (var i = 0; i < 100; i++)
        {
            lastWithinLimit = await client.GetAsync("/health");
        }

        lastWithinLimit!.StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...the next one is rejected.
        var overLimit = await client.GetAsync("/health");

        overLimit.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
