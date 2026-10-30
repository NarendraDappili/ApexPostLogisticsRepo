using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

/// <summary>
/// An immutable history entry for a manual stock correction: who changed which branch stock, by how much, and why.
/// </summary>
public sealed record StockAdjustment
{
    public const int MaxReasonLength = 200;

    public StockAdjustment(BranchCode branchCode, ProductId productId, int delta, string reason, StaffId changedBy, DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        ArgumentNullException.ThrowIfNull(changedBy);
        if (delta == 0) throw new ArgumentOutOfRangeException(nameof(delta), "A stock adjustment cannot be zero.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength) throw new ArgumentException($"An adjustment reason must be at most {MaxReasonLength} characters.", nameof(reason));

        BranchCode = branchCode;
        ProductId = productId;
        Delta = delta;
        Reason = trimmed;
        ChangedBy = changedBy;
        OccurredAtUtc = (occurredAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    // Used only when a stored history row is materialized; the stored time is already UTC.
    private StockAdjustment(BranchCode branchCode, ProductId productId, int delta, string reason, StaffId changedBy, DateTimeOffset occurredAtUtc)
        : this(branchCode, productId, delta, reason, changedBy, (DateTimeOffset?)occurredAtUtc)
    {
    }

    public BranchCode BranchCode { get; }

    public ProductId ProductId { get; }

    public int Delta { get; }

    public string Reason { get; }

    public StaffId ChangedBy { get; }

    public DateTimeOffset OccurredAtUtc { get; }
}
