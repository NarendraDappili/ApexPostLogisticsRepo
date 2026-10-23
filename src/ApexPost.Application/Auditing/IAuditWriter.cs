namespace ApexPost.Application.Auditing;

public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
