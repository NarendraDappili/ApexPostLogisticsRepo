namespace ApexPost.Application.Auditing;

public interface IAuditWriter : IAuditStore
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
