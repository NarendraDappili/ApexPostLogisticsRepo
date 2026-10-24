using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Repositories;

public sealed class JsonCounterTransactionRepository(JsonRepositoryOptions options) : ICounterTransactionRepository
{
    private readonly JsonFileStore<List<CounterTransactionDto>> _store = new(options, "counter-transactions.json");
    public async Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == id.Value)?.ToEntity();
    public Task SaveAsync(CounterTransaction transaction, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, transaction, static item => item.Id, CounterTransactionDto.From, cancellationToken);
    public async Task<IReadOnlyCollection<CounterTransaction>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Where(item => item.BranchCode == branchCode.Value).Select(item => item.ToEntity()).ToArray();
}

internal sealed record CounterTransactionLineDto(string? ProductId, string Description, int Quantity, decimal UnitPrice);
internal sealed record CounterTransactionDto(string Id, string BranchCode, string CounterCode, string ClerkId, List<CounterTransactionLineDto> Lines, decimal Subtotal, decimal Tax, PaymentMethod PaymentMethod, DateTimeOffset OccurredAtUtc, CounterTransactionStatus Status, string? StatusReason, string? AuthorizedBy)
{
    public static CounterTransactionDto From(CounterTransaction x) => new(x.Id.Value, x.BranchCode.Value, x.CounterCode.Value, x.ClerkId.Value, x.Lines.Select(line => new CounterTransactionLineDto(line.ProductId?.Value, line.Description, line.Quantity, line.UnitPrice.Amount)).ToList(), x.Subtotal.Amount, x.Tax.Amount, x.PaymentMethod, x.OccurredAtUtc, x.Status, x.StatusReason, x.AuthorizedBy?.Value);
    public CounterTransaction ToEntity()
    {
        var transaction = new CounterTransaction(new(Id), new(BranchCode), new(CounterCode), new(ClerkId), Lines.Select(line => new CounterTransactionLine(line.ProductId is null ? null : new ProductId(line.ProductId), line.Description, line.Quantity, new Money(line.UnitPrice))).ToArray(), new(Subtotal), new(Tax), PaymentMethod, OccurredAtUtc);
        if (Status == CounterTransactionStatus.Paid) transaction.MarkPaid();
        else if (Status == CounterTransactionStatus.Voided) transaction.Void(new StaffId(AuthorizedBy ?? throw new InvalidOperationException("Voided transaction lacks authorizer.")), StatusReason ?? throw new InvalidOperationException("Voided transaction lacks reason."));
        else if (Status == CounterTransactionStatus.Refunded)
        {
            transaction.MarkPaid();
            transaction.Refund(new StaffId(AuthorizedBy ?? throw new InvalidOperationException("Refunded transaction lacks authorizer.")), StatusReason ?? throw new InvalidOperationException("Refunded transaction lacks reason."));
        }
        return transaction;
    }
}
