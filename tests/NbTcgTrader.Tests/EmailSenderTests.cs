using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Email;
using Shouldly;

namespace NbTcgTrader.Tests;

// Unit tests for the three IEmailSender implementations (#69). No network and no
// provider account: FakeHttpMessageHandler stands in for Resend, and the file-drop
// sender writes to a temp directory.
public class EmailSenderTests
{
    private static EmailMessage Message() => new(
        To: "alice@cards.test",
        Subject: "Confirm your NB TCG Trader email address",
        HtmlBody: "<html><body>hello</body></html>",
        TextBody: "hello",
        Kind: EmailTemplates.VerificationKind);

    private static EmailOptions ResendOptions() => new()
    {
        Provider = EmailProvider.Resend,
        ApiKey = "re_test_key",
        FromAddress = "no-reply@nbtcg.example.com",
        FromName = "NB TCG Trader",
    };

    /// <summary>
    /// Sends one message through <see cref="ResendEmailSender"/> and returns both the
    /// result and the JSON the provider would have received. The body is captured
    /// inside the handler because HttpClient disposes the request content once the
    /// send completes — reading it afterwards is not reliable.
    /// </summary>
    private static async Task<(EmailSendResult Result, string? Body, Uri? Uri, int Calls)> SendViaResendAsync(
        EmailOptions options,
        string responseJson = """{"id":"provider-123"}""",
        HttpStatusCode status = HttpStatusCode.OK)
    {
        string? body = null;

        var handler = new FakeHttpMessageHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        var sender = new ResendEmailSender(
            http, Options.Create(options), NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync(Message(), CancellationToken.None);
        return (result, body, handler.LastRequestUri, handler.CallCount);
    }

    [Fact]
    public async Task Resend_posts_both_message_parts_to_the_emails_endpoint()
    {
        var (result, body, uri, calls) = await SendViaResendAsync(ResendOptions());

        result.Succeeded.ShouldBeTrue();
        result.ProviderMessageId.ShouldBe("provider-123");
        calls.ShouldBe(1);
        uri!.AbsolutePath.ShouldBe("/emails");

        var payload = JsonDocument.Parse(body!).RootElement;
        payload.GetProperty("from").GetString()
            .ShouldBe("NB TCG Trader <no-reply@nbtcg.example.com>");
        payload.GetProperty("to")[0].GetString().ShouldBe("alice@cards.test");
        // Both parts are sent: HTML-only mail is a deliverability and accessibility
        // problem, and some clients render only the text part.
        payload.GetProperty("html").GetString().ShouldBe("<html><body>hello</body></html>");
        payload.GetProperty("text").GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Resend_drops_a_display_name_that_could_split_the_from_header()
    {
        var options = ResendOptions();
        options.FromName = "Evil\r\nBcc: victim@example.com";

        var (_, body, _, _) = await SendViaResendAsync(options);

        // Falls back to the bare address rather than emitting a header that whoever
        // controls configuration could extend with extra recipients.
        JsonDocument.Parse(body!).RootElement.GetProperty("from").GetString()
            .ShouldBe("no-reply@nbtcg.example.com");
    }

    [Fact]
    public async Task Resend_reports_a_rejection_instead_of_throwing()
    {
        var (result, _, _, _) = await SendViaResendAsync(
            ResendOptions(),
            """{"message":"quota exceeded"}""",
            HttpStatusCode.TooManyRequests);

        // A provider failure must never surface as a 500 on registration.
        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldContain("429");
    }

    [Fact]
    public async Task Resend_fails_fast_without_an_api_key_and_makes_no_request()
    {
        var options = ResendOptions();
        options.ApiKey = null;

        var (result, _, _, calls) = await SendViaResendAsync(options);

        result.Succeeded.ShouldBeFalse();
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task File_drop_writes_the_rendered_message_and_sends_nothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nbtcg-email-{Guid.NewGuid():N}");
        try
        {
            var sender = new FileDropEmailSender(
                Options.Create(new EmailOptions { DropDirectory = directory }),
                new FakeHostEnvironment(),
                NullLogger<FileDropEmailSender>.Instance);

            var result = await sender.SendAsync(Message(), CancellationToken.None);

            result.Succeeded.ShouldBeTrue();
            var file = Directory.GetFiles(directory).ShouldHaveSingleItem();
            var contents = await File.ReadAllTextAsync(file, CancellationToken.None);

            // Everything a developer needs to finish the flow locally: who it was for,
            // what it said, and the body with its clickable link.
            contents.ShouldContain("alice@cards.test");
            contents.ShouldContain("Confirm your NB TCG Trader email address");
            contents.ShouldContain("<html><body>hello</body></html>");

            // The file name must not leak the recipient into a directory listing.
            Path.GetFileName(file).ShouldNotContain("alice");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Disabled_sender_accepts_the_message_and_delivers_nothing()
    {
        var sender = new DisabledEmailSender(NullLogger<DisabledEmailSender>.Instance);

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        // The kill switch must not become an error path: a user who asks for a reset
        // while email is off still gets a normal 202.
        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>Minimal IHostEnvironment; the file-drop sender only reads ContentRootPath.</summary>
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "NbTcgTrader.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
