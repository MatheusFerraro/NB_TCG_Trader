namespace NbTcgTrader.Tests;

/// <summary>
/// Throwaway JWT settings shared by the test hosts. Not a secret — it only signs
/// tokens for the in-process test server. The key is comfortably over 32 bytes so
/// it satisfies the startup validation in <c>AuthenticationExtensions</c>.
/// </summary>
public static class TestJwt
{
    public const string SigningKey = "test-signing-key-for-nb-tcg-trader-integration-tests-0123456789";

    public const string Issuer = "nb-tcg-trader-tests";

    public const string Audience = "nb-tcg-trader-tests";
}
