using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace NbTcgTrader.Tests;

// Integration tests for the cross-cutting pipeline wired in Program.cs (#3):
// health endpoint, and the ProblemDetails error contract on a failure path.
[Collection(IntegrationTestCollection.Name)]
public class CrossCuttingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CrossCuttingTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("application/problem+json");
    }

    // The test host runs in Development, where Cors:AllowedOrigins = [http://localhost:5173].

    [Fact]
    public async Task Cors_preflight_from_allowed_origin_gets_allow_origin_header()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldContain("http://localhost:5173");
    }

    [Fact]
    public async Task Cors_preflight_from_disallowed_origin_gets_no_allow_origin_header()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
