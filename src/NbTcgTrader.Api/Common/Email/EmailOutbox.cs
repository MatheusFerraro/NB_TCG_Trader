using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// In-process outbox: a bounded channel between the request thread (which enqueues)
/// and <see cref="EmailBackgroundService"/> (which sends). Bounded on purpose — an
/// unbounded queue turns a provider outage into unbounded memory growth. When it
/// fills, the newest message is dropped with a warning rather than blocking a request.
/// </summary>
/// <remarks>
/// Deliberately not durable: a container restart loses queued mail. That is an
/// accepted MVP trade-off — every email here is re-requestable by the user (resend
/// verification, request another reset link). A durable outbox table is the upgrade
/// path if delivery ever has to be guaranteed.
/// </remarks>
public sealed class EmailOutbox : IEmailDispatcher
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailOutbox> _logger;

    public EmailOutbox(IOptions<EmailOptions> options, ILogger<EmailOutbox> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<EmailMessage>(
            new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public bool Enqueue(EmailMessage message)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        _logger.LogWarning(
            "Email outbox is full; dropped the {Kind} email. The user can request it again.",
            message.Kind);

        return false;
    }

    /// <summary>Signals the reader to drain and stop; called on shutdown.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
