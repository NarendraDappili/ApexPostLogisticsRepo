using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Abstractions;

/// <summary>
/// Supplies the identity and branch context of the current operator without coupling Application to HTTP authentication.
/// </summary>
public interface ICurrentStaffContext
{
    StaffId? StaffId { get; }

    BranchCode? BranchCode { get; }
}
