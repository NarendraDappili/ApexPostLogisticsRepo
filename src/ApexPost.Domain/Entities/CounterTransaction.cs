using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class CounterTransaction : IEquatable<CounterTransaction>
{
    public CounterTransaction(CounterTransactionId id, BranchCode branchCode, CounterCode counterCode, StaffId clerkId, IReadOnlyCollection<CounterTransactionLine> lines, Money subtotal, Money tax, PaymentMethod paymentMethod, DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(counterCode);
        ArgumentNullException.ThrowIfNull(clerkId);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(subtotal);
        ArgumentNullException.ThrowIfNull(tax);
        if (lines.Count == 0) throw new ArgumentException("A transaction requires at least one line.", nameof(lines));
        if (!Enum.IsDefined(paymentMethod)) throw new ArgumentOutOfRangeException(nameof(paymentMethod));

        Id = id; BranchCode = branchCode; CounterCode = counterCode; ClerkId = clerkId;
        Lines = lines.ToArray(); Subtotal = subtotal; Tax = tax;
        Payment = new Payment(paymentMethod, Total);
        Status = CounterTransactionStatus.Open;
        OccurredAtUtc = (occurredAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public CounterTransactionId Id { get; }
    public BranchCode BranchCode { get; }
    public CounterCode CounterCode { get; }
    public StaffId ClerkId { get; }
    public IReadOnlyList<CounterTransactionLine> Lines { get; }
    public Money Subtotal { get; }
    public Money Tax { get; }
    public Money Total => new(checked(Subtotal.Amount + Tax.Amount));
    /// <summary>The settlement of the whole basket: one payment for the full total.</summary>
    public Payment Payment { get; }

    public PaymentMethod PaymentMethod => Payment.Method;
    public DateTimeOffset OccurredAtUtc { get; }
    public CounterTransactionStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public StaffId? AuthorizedBy { get; private set; }

    public void MarkPaid()
    {
        if (Status != CounterTransactionStatus.Open) throw new InvalidOperationException("Only an open transaction can be paid.");
        Status = CounterTransactionStatus.Paid;
    }

    public void Void(StaffId managerId, string reason)
    {
        EnsureManagerAction(managerId, reason);
        // Checkout persists a sale as Paid, so a void must cancel a Paid sale as well as an Open one.
        if (Status is not (CounterTransactionStatus.Open or CounterTransactionStatus.Paid)) throw new InvalidOperationException("Only an open or paid transaction can be voided.");
        Status = CounterTransactionStatus.Voided; StatusReason = reason.Trim(); AuthorizedBy = managerId;
    }

    public void Refund(StaffId managerId, string reason)
    {
        EnsureManagerAction(managerId, reason);
        if (Status != CounterTransactionStatus.Paid) throw new InvalidOperationException("Only a paid transaction can be refunded.");
        Status = CounterTransactionStatus.Refunded; StatusReason = reason.Trim(); AuthorizedBy = managerId;
    }

    private static void EnsureManagerAction(StaffId managerId, string reason)
    {
        ArgumentNullException.ThrowIfNull(managerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
    }

    public bool Equals(CounterTransaction? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => obj is CounterTransaction other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
}

public sealed record CounterTransactionLine
{
    public CounterTransactionLine(ProductId? productId, string description, int quantity, Money unitPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentNullException.ThrowIfNull(unitPrice);
        ProductId = productId;
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
    public ProductId? ProductId { get; }
    public string Description { get; }
    public int Quantity { get; }
    public Money UnitPrice { get; }
    public bool IsRetail => ProductId is not null;
    public Money Total => new(checked(UnitPrice.Amount * Quantity));
}
