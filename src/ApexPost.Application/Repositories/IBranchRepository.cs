using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

// Repositories only stage changes; IUnitOfWork.SaveChangesAsync commits them. Update marks an aggregate
// that this use case loaded and changed, so implementations without change tracking still persist it.

public interface IBranchRepository
{
    Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(Branch branch);

    void Update(Branch branch);
}
