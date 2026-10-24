namespace ApexPost.Application.Auditing;

/// <summary>
/// Stores audit records through append and read operations only. It deliberately
/// exposes no update or delete operation.
/// </summary>
public interface IAuditStore
{
    Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default);
}
