using ApexPost.Application.Notifications;

namespace ApexPost.Web.Services;

/// <summary>Local-only notification simulation; it records no document or payment details.</summary>
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Simulated notification to {Recipient}: {Subject}", message.Recipient, message.Subject);
        return Task.CompletedTask;
    }
}
