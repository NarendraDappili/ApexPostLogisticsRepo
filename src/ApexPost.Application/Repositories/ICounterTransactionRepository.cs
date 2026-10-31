using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface ICounterTransactionRepository
{
    Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken cancellationToken = default);
    void Add(CounterTransaction transaction);
    void Update(CounterTransaction transaction);
}
