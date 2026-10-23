using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Transactions;

public interface ITaxPolicy
{
    Money Calculate(Money subtotal);
}
