using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IMoneyOrderRepository
{
    Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    void Add(MoneyOrder moneyOrder);

    void Update(MoneyOrder moneyOrder);
}
