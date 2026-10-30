using ApexPost.Application.Abstractions;
using ApexPost.Application.BranchManagement;
using ApexPost.Application.Repositories;
using ApexPost.Application.StaffManagement;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Management;

public sealed class ManagementServiceTests
{
    [Fact]
    public async Task Staff_can_be_added_with_a_hashed_password_updated_and_deactivated()
    {
        var staff = new StaffRepo();
        var service = new StaffManagementService(staff, new BranchRepo(new Branch(new("BR-A"), "A"), new Branch(new("BR-B"), "B")), new FakeHasher());

        var created = await service.CreateAsync(new("stf-9", "New Clerk", "new.clerk@example.test", StaffRole.CounterClerk, "BR-A"), "initial-password");
        Assert.Equal("hashed:initial-password", created.PasswordHash);
        Assert.Equal("new.clerk@example.test", created.Email);

        var updated = await service.UpdateAsync(new("STF-9", "Promoted", "promoted@example.test", StaffRole.BranchManager, "BR-B"), newPassword: null);
        Assert.Equal((StaffRole.BranchManager, "BR-B", "hashed:initial-password"), (updated.Role, updated.AssignedBranchCode.Value, updated.PasswordHash));

        var deactivated = await service.SetActiveAsync(new("STF-9"), active: false, actingStaffId: new("STF-ADMIN"));
        Assert.False(deactivated.IsActive);
        Assert.False((await staff.FindByIdAsync(new("STF-9")))!.IsActive);
    }

    [Fact]
    public async Task Staff_rules_reject_duplicates_unknown_branches_weak_passwords_and_self_deactivation()
    {
        var staff = new StaffRepo();
        var service = new StaffManagementService(staff, new BranchRepo(new Branch(new("BR-A"), "A")), new FakeHasher());
        await service.CreateAsync(new("STF-1", "Clerk", "clerk@example.test", StaffRole.CounterClerk, "BR-A"), "long-enough-password");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new("STF-1", "Again", "again@example.test", StaffRole.CounterClerk, "BR-A"), "long-enough-password"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new("STF-2", "Lost", "lost@example.test", StaffRole.CounterClerk, "BR-NOWHERE"), "long-enough-password"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new("STF-3", "Weak", "weak@example.test", StaffRole.CounterClerk, "BR-A"), "short"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetActiveAsync(new("STF-1"), active: false, actingStaffId: new("STF-1")));
        await Assert.ThrowsAsync<StaffMemberNotFoundException>(() => service.UpdateAsync(new("STF-404", "Ghost", "ghost@example.test", StaffRole.Auditor, "BR-A"), null));
    }

    [Fact]
    public async Task Branches_are_created_with_counters_updated_and_closed()
    {
        var branches = new BranchRepo();
        var counters = new CounterRepo();
        var service = new BranchManagementService(branches, counters);

        var created = await service.CreateAsync(new("br-new", "New Branch", "Coast", "Mon-Fri 09:00-17:00"), counterCount: 3);
        Assert.Equal(["C-01", "C-02", "C-03"], counters.Items.Where(counter => counter.BranchCode == created.Code).Select(counter => counter.Code.Value));

        var updated = await service.UpdateAsync(new("BR-NEW", "Renamed", "Hills", "Sat 09:00-12:00"));
        Assert.Equal(("Renamed", "Hills"), (updated.Name, updated.Region));

        var closed = await service.SetOpenAsync(new("BR-NEW"), open: false);
        Assert.False(closed.CanAcceptTransactions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new("BR-NEW", "Duplicate", "Coast", "Any"), 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(new("BR-ZERO", "No counters", "Coast", "Any"), 0));
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string? passwordHash) => passwordHash == Hash(password);
    }

    private sealed class StaffRepo : IStaffMemberRepository
    {
        private readonly Dictionary<string, StaffMember> _items = [];
        public Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(id.Value));
        public Task<IReadOnlyCollection<StaffMember>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<StaffMember>>(_items.Values.ToArray());
        public Task SaveAsync(StaffMember staffMember, CancellationToken cancellationToken = default) { _items[staffMember.Id.Value] = staffMember; return Task.CompletedTask; }
    }

    private sealed class BranchRepo(params Branch[] branches) : IBranchRepository
    {
        private readonly Dictionary<string, Branch> _items = branches.ToDictionary(branch => branch.Code.Value);
        public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(code.Value));
        public Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Branch>>(_items.Values.ToArray());
        public Task SaveAsync(Branch branch, CancellationToken cancellationToken = default) { _items[branch.Code.Value] = branch; return Task.CompletedTask; }
    }

    private sealed class CounterRepo : ICounterRepository
    {
        public List<Counter> Items { get; } = [];
        public Task<Counter?> FindByCodeAsync(BranchCode branchCode, CounterCode counterCode, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(counter => counter.BranchCode == branchCode && counter.Code == counterCode));
        public Task<IReadOnlyCollection<Counter>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Counter>>(Items.Where(counter => counter.BranchCode == branchCode).ToArray());
        public Task SaveAsync(Counter counter, CancellationToken cancellationToken = default) { Items.Add(counter); return Task.CompletedTask; }
    }
}
