using ApexPost.Application.Events;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Repositories;
using ApexPost.Application.Notifications;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.UnitTests.Application.MoneyOrders;

public sealed class MoneyOrderWorkflowServiceTests
{
    [Theory]
    [InlineData(99, false)]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public async Task CreateAsync_AmountAroundTheThreshold_RequiresVerificationOnlyAboveIt(decimal amount, bool expectedRequirement)
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
    public async Task CreateAsync_FlatPlusPercentagePolicy_RoundsTheFeeToCents()
    {
        var orders = new InMemoryMoneyOrderRepository();
        var policy = new MoneyOrderPolicy(new Money(1000m), MoneyOrderPolicy.FlatPlusPercentageFee(new Money(2.50m), 0.01m));
        var service = new MoneyOrderWorkflowService(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A")), orders, policy, new StubIdentityVerifier(), new SpyUnitOfWork(), new FakeTimeProvider(TestTime.Now));

        var result = await service.CreateAsync(Request(123.45m));

        Assert.Equal(3.73m, result.MoneyOrder.Fee.Amount);
        Assert.Equal(127.18m, result.Total.Amount);
    }

    [Fact]
    public async Task VerifyIdentityAsync_VerifierAccepts_StoresVerifiedWithOnlyAMaskedDocumentReference()
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
    public async Task VerifyIdentityAsync_FailedThenRetried_VerifiesAndRefusesAnotherAttempt()
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
    public async Task VerifyIdentityAsync_OtherBranchMissingOrderOrBlankDocument_Throws()
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
    public async Task CreateAsync_NonPositiveAmount_ThrowsOutOfRange(decimal amount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(amount)));
    }

    [Fact]
    public async Task CreateAsync_BlankSenderAndRecipient_ReportsBothErrorsTogether()
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(10m) with { Sender = " ", Recipient = "" }));

        Assert.Contains("Sender is required.", exception.Errors);
        Assert.Contains("Recipient is required.", exception.Errors);
    }

    [Fact]
    public async Task CreateAsync_CancelledOrSaveFails_PropagatesTheException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(new InMemoryMoneyOrderRepository(), 100m).CreateAsync(Request(10m), cancellation.Token));

        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m, unitOfWork: new SpyUnitOfWork { FailWith = new IOException("Cannot write") });
        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(Request(10m)));
    }

    [Fact]
    public async Task CreateAsync_HighValueOrder_SendsOneSimulatedNotification()
    {
        var notifications = new RecordingNotificationSender();
        var service = CreateService(new InMemoryMoneyOrderRepository(), 100m, notifications);
        await service.CreateAsync(Request(50m));
        await service.CreateAsync(Request(101m) with { MoneyOrderId = "MO-2" });

        Assert.Single(notifications.Messages);
        Assert.Contains("high-value", notifications.Messages[0].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MO-2", notifications.Messages[0].Body, StringComparison.Ordinal);
    }

    // Week 5 (S10a): the coverage report showed this refusal was never reached by any test.
    [Fact]
    public async Task CreateAsync_UnknownBranch_ThrowsBranchNotFoundAndStoresNothing()
    {
        // Arrange
        var orders = new InMemoryMoneyOrderRepository();
        var service = CreateService(orders, 100m);

        // Act
        var exception = await Assert.ThrowsAsync<MoneyOrderBranchNotFoundException>(() => service.CreateAsync(Request(50m) with { BranchCode = "BR-NOWHERE" }));

        // Assert
        Assert.Equal("The money-order branch was not found.", exception.Message);
        Assert.Null(await orders.FindByIdAsync(new MoneyOrderId("MO-1")));
    }

    private static MoneyOrderWorkflowService CreateService(InMemoryMoneyOrderRepository orders, decimal threshold, INotificationSender? notifications = null, IIdentityVerifier? verifier = null, SpyUnitOfWork? unitOfWork = null) =>
        new(new InMemoryBranchRepository(new Branch(new("BR-A"), "Branch A"), new Branch(new("BR-B"), "Branch B")), orders,
            new MoneyOrderPolicy(new Money(threshold), _ => new Money(2m)), verifier ?? new StubIdentityVerifier(), unitOfWork ?? new SpyUnitOfWork(), new FakeTimeProvider(TestTime.Now),
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
        public void Add(Branch branch) => _branches[branch.Code.Value] = branch;
        public void Update(Branch branch) => _branches[branch.Code.Value] = branch;
    }

    private sealed class InMemoryMoneyOrderRepository : IMoneyOrderRepository
    {
        private readonly Dictionary<string, MoneyOrder> _orders = [];
        public Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _orders.TryGetValue(id.Value, out var order);
            return Task.FromResult(order);
        }
        public Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<MoneyOrder>>(_orders.Values.Where(order => order.BranchCode == branchCode).ToArray());
        public void Add(MoneyOrder moneyOrder) => _orders[moneyOrder.Id.Value] = moneyOrder;
        public void Update(MoneyOrder moneyOrder) => _orders[moneyOrder.Id.Value] = moneyOrder;
    }

    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<NotificationMessage> Messages { get; } = [];
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; }
    }
}
