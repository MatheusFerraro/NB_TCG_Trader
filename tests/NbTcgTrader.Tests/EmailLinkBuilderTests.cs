using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Email;
using Shouldly;

namespace NbTcgTrader.Tests;

// The emailed links are the whole recovery flow (#69): if a token does not survive
// the round-trip through a query string, every reset silently fails. No network, no
// database — the builder is pure.
public class EmailLinkBuilderTests
{
    // Identity's real tokens are base64 with '+', '/' and '=' — exactly the characters
    // that a naive query string mangles. Use one here rather than a friendly stand-in.
    private const string IdentityLikeToken = "CfDJ8Ab+c/d=EF+gh/i==";

    private static EmailLinkBuilder Build(string frontendBaseUrl = "https://nbtcg.example.com") =>
        new(Options.Create(new EmailOptions { FrontendBaseUrl = frontendBaseUrl }));

    [Fact]
    public void Token_survives_the_base64url_round_trip()
    {
        var encoded = EmailLinkBuilder.EncodeToken(IdentityLikeToken);

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
        EmailLinkBuilder.TryDecodeToken(encoded).ShouldBe(IdentityLikeToken);
    }

    [Theory]
    [InlineData("not base64url!!")]
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_token_decodes_to_null_rather_than_throwing(string? encoded) =>
        // A hand-mangled link must become a 400, never an unhandled exception.
        EmailLinkBuilder.TryDecodeToken(encoded).ShouldBeNull();

    [Fact]
    public void Verify_link_points_at_the_configured_frontend_with_both_query_values()
    {
        var link = Build().BuildVerifyEmailLink("user-42", IdentityLikeToken);

        link.ShouldStartWith("https://nbtcg.example.com/verify-email?");
        link.ShouldContain("userId=user-42");

        var token = QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();
        EmailLinkBuilder.TryDecodeToken(token).ShouldBe(IdentityLikeToken);
    }

    [Fact]
    public void Reset_link_carries_the_email_and_a_decodable_token()
    {
        var link = Build().BuildResetPasswordLink("alice+tag@cards.test", IdentityLikeToken);

        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        // '+' in an address must arrive as '+', not as a space.
        query["email"].ToString().ShouldBe("alice+tag@cards.test");
        EmailLinkBuilder.TryDecodeToken(query["token"].ToString()).ShouldBe(IdentityLikeToken);
    }

    [Fact]
    public void Trailing_slash_on_the_base_url_does_not_double_up()
    {
        var link = Build("https://nbtcg.example.com/").BuildLoginLink();

        link.ShouldBe("https://nbtcg.example.com/login");
    }
}
