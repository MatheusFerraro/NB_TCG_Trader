namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Drains <see cref="EmailOutbox"/> and sends each message through the configured
/// <see cref="IEmailSender"/>, one at a time. Serial by design: the free tiers this
/// project targets rate-limit requests per second, and transactional volume here is
/// a trickle (issue #69).
/// </summary>
public sealed class EmailBackgroundService(
    EmailOutbox outbox,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in outbox.Reader.ReadAllAsync(stoppingToken))
            {
                await SendOneAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        outbox.Complete();
        return base.StopAsync(cancellationToken);
    }

    private async Task SendOneAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        try
        {
            // The sender is scoped (typed HttpClient), so each send gets its own scope.
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var result = await sender.SendAsync(message, stoppingToken);

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Sent {Kind} email to {Recipient} (provider id {ProviderMessageId})",
                    message.Kind,
                    message.To,
                    result.ProviderMessageId ?? "n/a");
            }
            else
            {
                // Already logged in detail by the sender; this records the give-up.
                logger.LogWarning(
                    "Giving up on the {Kind} email to {Recipient}: {Error}",
                    message.Kind,
                    message.To,
                    result.Error);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A single bad message must never take the drain loop down with it.
            logger.LogError(ex, "Unhandled failure sending the {Kind} email", message.Kind);
        }
    }
}
