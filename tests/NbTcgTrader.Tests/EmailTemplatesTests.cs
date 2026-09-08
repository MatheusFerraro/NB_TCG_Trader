using System.Net;
using NbTcgTrader.Api.Common.Email;
using Shouldly;

namespace NbTcgTrader.Tests;

// The templates are the one place user-supplied text (a display name) is spliced
// into markup that lands in someone's mail client, so encoding is the point of
// these tests, not the copy (CLAUDE.md §15, #69).
public class EmailTemplatesTests
{
    private const string Link = "https://nbtcg.example.com/verify-email?userId=u1&token=abc";

    [Fact]
    public void Verification_carries_the_link_in_both_parts_and_names_its_kind()
    {
        var message = EmailTemplates.Verification("alice@cards.test", "Alice", Link);

        message.To.ShouldBe("alice@cards.test");
        message.Kind.ShouldBe(EmailTemplates.VerificationKind);
        message.Subject.ShouldContain("Confirm");
        // In HTML the '&' between query parameters is escaped; the link is still
        // the same URL once the mail client parses it.
        message.HtmlBody.ShouldContain(WebUtility.HtmlEncode(Link));
        // The plain-text part must stand alone: clients that strip HTML still need
        // a copyable link, verbatim.
        message.TextBody.ShouldContain(Link);
        message.TextBody.ShouldContain("Alice");
    }

    [Fact]
    public void Password_reset_and_changed_notice_are_distinct_messages()
    {
        var reset = EmailTemplates.PasswordReset("alice@cards.test", "Alice", Link);
        var changed = EmailTemplates.PasswordChanged("alice@cards.test", "Alice", Link);

        reset.Kind.ShouldBe(EmailTemplates.PasswordResetKind);
        changed.Kind.ShouldBe(EmailTemplates.PasswordChangedKind);
        reset.Subject.ShouldNotBe(changed.Subject);
        // The security notice exists so an unexpected reset is visible; it must say
        // the password changed rather than invite another reset.
        changed.TextBody.ShouldContain("changed");
    }

    [Fact]
    public void Display_name_is_html_encoded_so_it_cannot_inject_markup()
    {
        var message = EmailTemplates.Verification(
            "mallory@cards.test", "<script>alert(1)</script>", Link);

        message.HtmlBody.ShouldNotContain("<script>");
        message.HtmlBody.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void Display_name_quotes_cannot_break_out_of_the_markup()
    {
        var message = EmailTemplates.PasswordReset(
            "mallory@cards.test", "\"><img src=x onerror=alert(1)>", Link);

        message.HtmlBody.ShouldNotContain("<img");
        message.HtmlBody.ShouldContain("&lt;img");
    }

    [Fact]
    public void Href_escapes_the_query_separator_but_keeps_the_url_intact()
    {
        var message = EmailTemplates.Verification("alice@cards.test", "Alice", Link);

        // Percent-encoding the href instead of HTML-escaping it would turn "https://"
        // into "https%3A%2F%2F" and produce a dead button — assert it did not.
        message.HtmlBody.ShouldContain("href=\"https://nbtcg.example.com/verify-email?");
        message.HtmlBody.ShouldContain("&amp;token=abc");
        message.HtmlBody.ShouldNotContain("https%3A%2F%2F");
    }

    [Fact]
    public void Every_template_produces_a_non_empty_html_and_text_part()
    {
        EmailMessage[] messages =
        [
            EmailTemplates.Verification("a@b.test", "A", Link),
            EmailTemplates.PasswordReset("a@b.test", "A", Link),
            EmailTemplates.PasswordChanged("a@b.test", "A", Link),
        ];

        foreach (var message in messages)
        {
            message.Subject.ShouldNotBeNullOrWhiteSpace();
            message.HtmlBody.ShouldContain("<html");
            message.TextBody.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
