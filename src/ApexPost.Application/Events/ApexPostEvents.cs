using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Events;

/// <summary>A business event raised by a workflow after the state change it describes has been persisted.</summary>
public abstract record ApexPostEvent
{
    /// <summary>
    /// When it happened, given by whoever raises the event from the injected <see cref="TimeProvider"/>. It is
    /// required, so no event reads the system clock by itself and a test can fix the time (TESTING-STRATEGY.md §8).
    /// </summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record TransactionCompleted(CounterTransaction Transaction) : ApexPostEvent;

public sealed record TransactionVoided(CounterTransaction Transaction) : ApexPostEvent;

public sealed record TransactionRefunded(CounterTransaction Transaction) : ApexPostEvent;

public sealed record StockLow(BranchCode BranchCode, ProductId ProductId, int Quantity, int Threshold) : ApexPostEvent;

public sealed record MoneyOrderIssued(MoneyOrder MoneyOrder, bool RequiresIdentityVerification) : ApexPostEvent;
