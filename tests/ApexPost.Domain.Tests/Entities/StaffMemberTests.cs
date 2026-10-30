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

    [Fact]
    public void Update_details_changes_role_branch_and_normalised_email()
    {
        var member = new StaffMember(new StaffId("staff-1"), "Avery", StaffRole.CounterClerk, new BranchCode("BR-001"), "avery@example.test");

        member.UpdateDetails(" Avery Jones ", " Avery.Jones@Example.TEST ", StaffRole.BranchManager, new BranchCode("BR-002"));

        Assert.Equal("Avery Jones", member.Name);
        Assert.Equal("avery.jones@example.test", member.Email);
        Assert.Equal(StaffRole.BranchManager, member.Role);
        Assert.Equal(new BranchCode("BR-002"), member.AssignedBranchCode);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.test")]
    [InlineData("has space@example.test")]
    [InlineData("trailing@")]
    public void Invalid_email_is_rejected_and_leaves_the_staff_member_unchanged(string email)
    {
        var member = new StaffMember(new StaffId("staff-1"), "Avery", StaffRole.CounterClerk, new BranchCode("BR-001"), "avery@example.test");

        Assert.ThrowsAny<ArgumentException>(() => member.UpdateDetails("Morgan", email, StaffRole.Auditor, new BranchCode("BR-002")));
        Assert.Equal("Avery", member.Name);
        Assert.Equal(StaffRole.CounterClerk, member.Role);
    }

    [Fact]
    public void Password_hash_is_stored_only_when_not_blank()
    {
        var member = new StaffMember(new StaffId("staff-1"), "Avery", StaffRole.CounterClerk, new BranchCode("BR-001"));

        Assert.Null(member.PasswordHash);
        Assert.ThrowsAny<ArgumentException>(() => member.SetPasswordHash(" "));
        member.SetPasswordHash("PBKDF2-SHA256$1$c2FsdA==$aGFzaA==");
        Assert.Equal("PBKDF2-SHA256$1$c2FsdA==$aGFzaA==", member.PasswordHash);
    }
}
