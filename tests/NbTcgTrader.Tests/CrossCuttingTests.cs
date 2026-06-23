using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace NbTcgTrader.Tests;

// Integration tests for the cross-cutting pipeline wired in Program.cs (#3):
// health endpoint, and the ProblemDetails error contract on a failure path.
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
}
