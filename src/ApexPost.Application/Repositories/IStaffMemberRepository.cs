using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IStaffMemberRepository
{
    Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StaffMember>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(StaffMember staffMember);

    void Update(StaffMember staffMember);
}
