using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface ICounterRepository
{
    Task<Counter?> FindByCodeAsync(
        BranchCode branchCode,
        CounterCode counterCode,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Counter counter, CancellationToken cancellationToken = default);
}
