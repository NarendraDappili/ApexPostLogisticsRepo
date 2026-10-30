using ApexPost.Application.Events;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Repositories;
using ApexPost.Application.Notifications;
using ApexPost.Application.Validation;
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

        var result = await service.CreateAsync(Request(amount));

        Assert.Equal(expectedRequirement, result.RequiresIdentityVerification);
        Assert.Equal(amount + 2m, result.Total.Amount);
        Assert.Equal(IdentityVerificationOutcome.Pending, result.MoneyOrder.IdentityVerificationOutcome);
        Assert.NotNull(await orders.FindByIdAsync(new("MO-1")));
    }

    [Fact]
    public async Task Fee_comes_from_the_configured_policy_and_is_rounded_to_cents()
    {
        var orders = new InMemoryMoneyOrderRepository();
        var policy = new MoneyOrderPolicy(new Money(1000m), MoneyOrderPolicy.FlatPlusPercentageFee(new Money(2.50m), 0.01m));
        var service = new MoneyOrderWorkflowService(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A")), orders, policy, new StubIdentityVerifier());

        var result = await service.CreateAsync(Request(123.45m));

        Assert.Equal(3.73m, result.MoneyOrder.Fee.Amount);
        Assert.Equal(127.18m, result.Total.Amount);
    }

    [Fact]
    public async Task Verifier_decides_the_outcome_and_only_a_masked_document_reference_is_kept()
    {
        var orders = new InMemoryMoneyOrderRepository();
        var service = CreateService(orders, 100m, verifier: new StubIdentityVerifier(IdentityVerificationOutcome.Verified));
        await service.CreateAsync(Request(101m));

        var verified = await service.VerifyIdentityAsync(new("MO-1"), new("BR-A"), new("Passport", "ABCD1234"));

        Assert.Equal(IdentityVerificationOutcome.Verified, verified.IdentityVerificationOutcome);
        Assert.Equal("****1234", verified.IdentityVerification!.MaskedDocumentReference.Value);
        Assert.Equal("Passport", verified.IdentityVerification.DocumentType);
        Assert.Equal("CTX-1", verified.IdentityVerification.ContextId.Value);
        Assert.Equal(IdentityVerificationOutcome.Verified, (await orders.FindByIdAsync(new("MO-1")))!.IdentityVerificationOutcome);
    }

    [Fact]
    public async Task Failed_verification_is_recorded_and_can_be_retried_but_a_verified_order_cannot_be_reverified()
    {
        var verifier = new StubIdentityVerifier(IdentityVerificationOutcome.Failed);
        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m, verifier: verifier);
        await service.CreateAsync(Request(101m));

        Assert.Equal(IdentityVerificationOutcome.Failed, (await service.VerifyIdentityAsync(new("MO-1"), new("BR-A"), new("Passport", "BAD00000"))).IdentityVerificationOutcome);
        verifier.Outcome = IdentityVerificationOutcome.Verified;
        Assert.Equal(IdentityVerificationOutcome.Verified, (await service.VerifyIdentityAsync(new("MO-1"), new("BR-A"), new("Passport", "GOOD1234"))).IdentityVerificationOutcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyIdentityAsync(new("MO-1"), new("BR-A"), new("Passport", "GOOD1234")));
    }

    [Fact]
    public async Task Verification_is_rejected_for_another_branch_a_missing_order_or_a_blank_document()
    {
        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m);
        await service.CreateAsync(Request(101m));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyIdentityAsync(new("MO-1"), new("BR-B"), new("Passport", "ABCD1234")));
        await Assert.ThrowsAsync<MoneyOrderNotFoundException>(() => service.VerifyIdentityAsync(new("MO-404"), new("BR-A"), new("Passport", "ABCD1234")));
        var invalid = await Assert.ThrowsAsync<RequestValidationException>(() => service.VerifyIdentityAsync(new("MO-1"), new("BR-A"), new(" ", "")));
        Assert.Equal(2, invalid.Errors.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_amounts_are_rejected(decimal amount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(amount)));
    }

    [Fact]
    public async Task Blank_request_fields_are_reported_together_by_the_attribute_validator()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(10m) with { Sender = " ", Recipient = "" }));

        Assert.Contains("Sender is required.", exception.Errors);
        Assert.Contains("Recipient is required.", exception.Errors);
    }

    [Fact]
    public async Task Cancellation_and_persistence_failures_are_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(10m), cancellation.Token));

        var service = CreateService(new InMemoryMoneyOrderRepository { SaveException = new IOException("Cannot write") }, 100m);
        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(Request(10m)));
    }

    [Fact]
    public async Task High_value_money_order_raises_an_issued_event_that_sends_a_simulated_notification()
    {
        var notifications = new RecordingNotificationSender();
        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m, notifications);
        await service.CreateAsync(Request(50m));
        await service.CreateAsync(Request(101m) with { MoneyOrderId = "MO-2" });

        Assert.Single(notifications.Messages);
        Assert.Contains("high-value", notifications.Messages[0].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MO-2", notifications.Messages[0].Body, StringComparison.Ordinal);
    }

    private static MoneyOrderWorkflowService CreateService(InMemoryMoneyOrderRepository orders, decimal threshold, INotificationSender? notifications = null, IIdentityVerifier? verifier = null) =>
        new(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A"), new Branch(new("BR-B"), "Branch B")), orders,
            new MoneyOrderPolicy(new Money(threshold), _ => new Money(2m)), verifier ?? new StubIdentityVerifier(),
            notifications is null ? null : new EventPublisher([new NotificationEventObserver(notifications, new NotificationRecipients())]));

    private static MoneyOrderCreationRequest Request(decimal amount) =>
        new("MO-1", "BR-A", amount, "Synthetic Sender", "Synthetic Recipient", "CTX-1");

    private sealed class StubIdentityVerifier(IdentityVerificationOutcome outcome = IdentityVerificationOutcome.Verified) : IIdentityVerifier
    {
        public IdentityVerificationOutcome Outcome { get; set; } = outcome;
        public IReadOnlyCollection<string> SupportedDocumentTypes => ["Passport"];
        public Task<IdentityVerificationOutcome> VerifyAsync(string documentType, string documentNumber, CancellationToken cancellationToken = default) => Task.FromResult(Outcome);
    }

    private sealed class InMemoryBranchRepository(params Branch[] branches) : IBranchRepository
    {
        private readonly Dictionary<string, Branch> _branches = branches.ToDictionary(branch => branch.Code.Value);
        public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _branches.TryGetValue(code.Value, out var branch);
            return Task.FromResult(branch);
        }
        public Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Branch>>(_branches.Values.ToArray());
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
        public Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<MoneyOrder>>(_orders.Values.Where(order => order.BranchCode == branchCode).ToArray());
        public Task SaveAsync(MoneyOrder moneyOrder, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SaveException is not null) throw SaveException;
            _orders[moneyOrder.Id.Value] = moneyOrder;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<NotificationMessage> Messages { get; } = [];
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
    }
}
