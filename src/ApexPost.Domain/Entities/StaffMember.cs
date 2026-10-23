using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class StaffMember : IEquatable<StaffMember>
{
    public StaffMember(StaffId id, string name, StaffRole role, BranchCode assignedBranchCode)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assignedBranchCode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)role, (int)StaffRole.Administrator);
        ArgumentOutOfRangeException.ThrowIfNegative((int)role);

        Id = id;
        Name = name.Trim();
        Role = role;
        AssignedBranchCode = assignedBranchCode;
        IsActive = true;
    }

    public StaffId Id { get; }

    public string Name { get; }

    public StaffRole Role { get; }

    public BranchCode AssignedBranchCode { get; }

    public bool IsActive { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool CanProcessTransactionsAt(BranchCode branchCode)
    {
        ArgumentNullException.ThrowIfNull(branchCode);

        return IsActive && (Role != StaffRole.CounterClerk || AssignedBranchCode == branchCode);
    }

    public bool Equals(StaffMember? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is StaffMember other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
