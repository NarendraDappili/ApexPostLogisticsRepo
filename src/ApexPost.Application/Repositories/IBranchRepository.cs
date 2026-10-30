using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IBranchRepository
{
    Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Branch branch, CancellationToken cancellationToken = default);
}
