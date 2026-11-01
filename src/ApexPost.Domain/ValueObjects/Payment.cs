using ApexPost.Domain.Enums;

namespace ApexPost.Domain.ValueObjects;

/// <summary>How a counter transaction was settled. The amount always equals the transaction total, which must be positive.</summary>
public sealed record Payment
{
    public Payment(PaymentMethod method, Money amount)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "A payment must be greater than zero.");

        Method = method;
        Amount = amount;
    }

    public PaymentMethod Method { get; }

    public Money Amount { get; }
}
