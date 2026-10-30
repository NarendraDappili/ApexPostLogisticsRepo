using ApexPost.Application.Events;

namespace ApexPost.Application.Notifications;

/// <summary>Recipients for simulated notifications.</summary>
public sealed class NotificationRecipients
{
    public string Operations { get; init; } = "operations-alerts@example.test";

    public string Compliance { get; init; } = "compliance-alerts@example.test";

    public string Inventory { get; init; } = "operations-alerts@example.test";
}

/// <summary>Turns the events the brief requires a notification for (low stock, high-value money order, void, refund) into messages.</summary>
public sealed class NotificationEventObserver(INotificationSender sender, NotificationRecipients recipients) : IEventObserver
{
    public Task OnEventAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default)
    {
        NotificationMessage? message = applicationEvent switch
        {
            StockLow low => new(recipients.Inventory, "ApexPost low-stock alert",
                $"{low.ProductId.Value} at {low.BranchCode.Value} has {low.Quantity} units available."),
            MoneyOrderIssued { RequiresIdentityVerification: true } issued => new(recipients.Compliance, "ApexPost high-value money order",
                $"Money order {issued.MoneyOrder.Id.Value} at {issued.MoneyOrder.BranchCode.Value} was issued and requires identity verification before completion."),
            TransactionVoided { Transaction: var voided } => Reversal(voided),
            TransactionRefunded { Transaction: var refunded } => Reversal(refunded),
            _ => null
        };

        return message is null ? Task.CompletedTask : sender.SendAsync(message, cancellationToken);
    }

    private NotificationMessage Reversal(Domain.Entities.CounterTransaction transaction) => new(
        recipients.Operations,
        $"ApexPost transaction {transaction.Status}",
        $"Transaction {transaction.Id.Value} at {transaction.BranchCode.Value} was {transaction.Status.ToString().ToLowerInvariant()}.");
}
