using System.Threading.Channels;

namespace Rolehound.Api.Infrastructure.Email;

/// <summary>Hands an email to a background sender, so the request does not wait for SMTP.</summary>
public interface IEmailOutbox
{
    void Enqueue(EmailMessage message);
}

/// <summary>
/// In-memory queue drained by one background loop. Used where the request must
/// answer the same way, in the same time, whether or not an email goes out
/// (forgot password). A message lost on restart is acceptable there: the person
/// asks again. Send failures are logged, never shown to the requester.
/// </summary>
internal sealed partial class EmailOutbox(IEmailSender sender, ILogger<EmailOutbox> logger) : BackgroundService, IEmailOutbox
{
    private readonly Channel<EmailMessage> _queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Enqueue(EmailMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
        {
            LogDropped(logger);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email outbox is full; message dropped")]
    private static partial void LogDropped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Queued email could not be sent")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
