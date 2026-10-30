using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Events;

/// <summary>A business event raised by a workflow after the state change it describes has been persisted.</summary>
public abstract record ApexPostEvent
{
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TransactionCompleted(CounterTransaction Transaction) : ApexPostEvent;

public sealed record TransactionVoided(CounterTransaction Transaction) : ApexPostEvent;

public sealed record TransactionRefunded(CounterTransaction Transaction) : ApexPostEvent;

public sealed record StockLow(BranchCode BranchCode, ProductId ProductId, int Quantity, int Threshold) : ApexPostEvent;

public sealed record MoneyOrderIssued(MoneyOrder MoneyOrder, bool RequiresIdentityVerification) : ApexPostEvent;
