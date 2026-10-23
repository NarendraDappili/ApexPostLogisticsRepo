using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class StaffMemberTests
{
    [Fact]
    public void Active_counter_clerk_can_transact_at_assigned_branch_only()
    {
        var clerk = new StaffMember(
            new StaffId("staff-1"),
            "Avery",
            StaffRole.CounterClerk,
            new BranchCode("br-001"));

        Assert.True(clerk.CanProcessTransactionsAt(new BranchCode("BR-001")));
        Assert.False(clerk.CanProcessTransactionsAt(new BranchCode("BR-002")));
    }

    [Fact]
    public void Inactive_staff_cannot_process_transactions()
    {
        var staff = new StaffMember(
            new StaffId("staff-1"),
            "Avery",
            StaffRole.BranchManager,
            new BranchCode("br-001"));

        staff.Deactivate();

        Assert.False(staff.CanProcessTransactionsAt(new BranchCode("BR-001")));
        staff.Activate();
        Assert.True(staff.CanProcessTransactionsAt(new BranchCode("BR-002")));
    }

    [Fact]
    public void Staff_members_with_same_identifier_compare_equal()
    {
        Assert.Equal(
            new StaffMember(new StaffId("staff-1"), "Avery", StaffRole.CounterClerk, new BranchCode("BR-001")),
            new StaffMember(new StaffId("STAFF-1"), "Morgan", StaffRole.Auditor, new BranchCode("BR-002")));
    }
}
