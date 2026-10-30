using ApexPost.Domain.Enums;

namespace ApexPost.Domain.ValueObjects;

/// <summary>How a counter transaction was settled. The amount always equals the transaction total.</summary>
public sealed record Payment
{
    public Payment(PaymentMethod method, Money amount)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        ArgumentNullException.ThrowIfNull(amount);

        Method = method;
        Amount = amount;
    }

    public PaymentMethod Method { get; }

    public Money Amount { get; }
}
