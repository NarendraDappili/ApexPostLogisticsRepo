using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.MoneyOrders;

public sealed class MoneyOrderWorkflowServiceTests
{
    [Theory]
    [InlineData(99, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public async Task Verification_is_required_only_above_the_configured_threshold(decimal amount, bool expectedRequirement)
    {
        var orders = new InMemoryMoneyOrderRepository();
        var service = CreateService(orders, 100m);
        var request = Request(amount, expectedRequirement ? Verified("CTX-1") : null);

        var result = await service.CreateAsync(request);

        Assert.Equal(expectedRequirement, result.RequiresIdentityVerification);
        Assert.Equal(amount + 2m, result.Total.Amount);
        Assert.NotNull(await orders.FindByIdAsync(new("MO-1")));
    }

    [Theory]
    [InlineData(IdentityVerificationOutcome.Failed)]
    [InlineData(IdentityVerificationOutcome.Pending)]
    public async Task High_value_orders_reject_unsuccessful_verification(IdentityVerificationOutcome outcome)
    {
        var orders = new InMemoryMoneyOrderRepository();
        var service = CreateService(orders, 100m);

        await Assert.ThrowsAsync<IdentityVerificationRequiredException>(() => service.CreateAsync(Request(101m, Verified("CTX-1") with { Outcome = outcome })));
        Assert.Null(await orders.FindByIdAsync(new("MO-1")));
    }

    [Fact]
    public async Task High_value_orders_reject_missing_or_different_context_verification()
    {
        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m);

        await Assert.ThrowsAsync<IdentityVerificationRequiredException>(() => service.CreateAsync(Request(101m, null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Request(101m, Verified("OTHER-CONTEXT"))));
    }

    [Fact]
    public async Task Verification_keeps_only_a_masked_document_reference()
    {
        var orders = new InMemoryMoneyOrderRepository();
        var service = CreateService(orders, 100m);

        var result = await service.CreateAsync(Request(101m, Verified("CTX-1") with { DocumentNumber = "ABCD1234" }));

        Assert.Equal("****1234", result.MoneyOrder.IdentityVerification!.MaskedDocumentReference.Value);
        Assert.DoesNotContain("ABCD1234", result.MoneyOrder.IdentityVerification.MaskedDocumentReference.Value, StringComparison.Ordinal);
        Assert.Equal("Demo ID", result.MoneyOrder.IdentityVerification.DocumentType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_amounts_are_rejected(decimal amount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(amount, null)));
    }

    [Fact]
    public async Task Cancellation_and_persistence_failures_are_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(10m, null), cancellation.Token));

        var service = CreateService(new InMemoryMoneyOrderRepository { SaveException = new IOException("Cannot write") }, 100m);
        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(Request(10m, null)));
    }

    private static MoneyOrderWorkflowService CreateService(InMemoryMoneyOrderRepository orders, decimal threshold) =>
        new(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A")), orders, new MoneyOrderPolicy(new Money(threshold)));

    private static MoneyOrderCreationRequest Request(decimal amount, IdentityVerificationRequest? verification) =>
        new("MO-1", "BR-A", amount, "Synthetic Sender", "Synthetic Recipient", 2m, "CTX-1", verification);

    private static IdentityVerificationRequest Verified(string context) => new(context, "Demo ID", "DOC-1234", IdentityVerificationOutcome.Verified);

    private sealed class InMemoryBranchRepository(params Branch[] branches) : IBranchRepository
    {
        private readonly Dictionary<string, Branch> _branches = branches.ToDictionary(branch => branch.Code.Value);
        public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _branches.TryGetValue(code.Value, out var branch);
            return Task.FromResult(branch);
        }
        public Task SaveAsync(Branch branch, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryMoneyOrderRepository : IMoneyOrderRepository
    {
        private readonly Dictionary<string, MoneyOrder> _orders = [];
        public Exception? SaveException { get; init; }
        public Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _orders.TryGetValue(id.Value, out var order);
            return Task.FromResult(order);
        }
        public Task SaveAsync(MoneyOrder moneyOrder, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SaveException is not null) throw SaveException;
            _orders[moneyOrder.Id.Value] = moneyOrder;
            return Task.CompletedTask;
        }
    }
}
