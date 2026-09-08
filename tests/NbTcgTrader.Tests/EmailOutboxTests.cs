using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Email;
using Shouldly;

namespace NbTcgTrader.Tests;

// The outbox is what keeps provider latency off the request thread (#69). Its two
// contractual promises: enqueue never blocks, and a full queue degrades to a dropped
// message rather than a failed request.
public class EmailOutboxTests
{
    private static EmailMessage Message(string kind = "email-verification") =>
        new("alice@cards.test", "Subject", "<html></html>", "text", kind);

    private static EmailOutbox Create(int capacity) =>
        new(Options.Create(new EmailOptions { QueueCapacity = capacity }),
            NullLogger<EmailOutbox>.Instance);

    [Fact]
    public async Task Enqueued_messages_are_readable_in_order()
    {
        var outbox = Create(capacity: 8);

        outbox.Enqueue(Message("email-verification")).ShouldBeTrue();
        outbox.Enqueue(Message("password-reset")).ShouldBeTrue();
        outbox.Complete();

        var kinds = new List<string>();
        await foreach (var message in outbox.Reader.ReadAllAsync(CancellationToken.None))
        {
            kinds.Add(message.Kind);
        }

        kinds.ShouldBe(new[] { "email-verification", "password-reset" });
    }

    [Fact]
    public void A_full_outbox_drops_the_message_instead_of_blocking()
    {
        var outbox = Create(capacity: 1);

        outbox.Enqueue(Message()).ShouldBeTrue();

        // Nothing is draining, so the second write has nowhere to go. It must report
        // the drop and return — blocking here would stall an HTTP request, and every
        // message this app sends can be re-requested by the user.
        outbox.Enqueue(Message()).ShouldBeFalse();
    }

    [Fact]
    public void Completing_the_outbox_twice_is_harmless()
    {
        var outbox = Create(capacity: 1);

        outbox.Complete();

        // StopAsync can run after the drain loop already completed the channel.
        Should.NotThrow(() => outbox.Complete());
    }
}
