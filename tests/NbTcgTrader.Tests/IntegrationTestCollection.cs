namespace NbTcgTrader.Tests;

// WebApplicationFactory invokes the shared static Program entry point (and the
// static Serilog.Log.Logger). Running such tests in parallel races inside
// HostFactoryResolver, so this collection serializes them. Each class still owns
// its own factory via IClassFixture — keeping the rate-limiter bucket isolated.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection
{
    public const string Name = "Integration";
}
