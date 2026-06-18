using Shouldly;

namespace NbTcgTrader.Tests;

// Smoke test verifying the xUnit + Shouldly test harness is wired up.
// Replace with real slice tests (import matching, validators, marketplace
// filters) as features land — see CLAUDE.md §11.
public class TestHarnessSmokeTests
{
    [Fact]
    public void Shouldly_assertions_are_available()
    {
        var sum = 2 + 2;

        sum.ShouldBe(4);
    }
}
