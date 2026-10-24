using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// JSON-backed, append-only audit store. The shared file store serializes append
/// operations in this process and uses a complete replacement write to avoid a
/// partially written JSON document.
/// </summary>
public sealed class JsonAuditStore : IAuditStore
{
    private readonly JsonFileStore<List<AuditRecord>> _store;

    public JsonAuditStore(JsonRepositoryOptions options) => _store = new JsonFileStore<List<AuditRecord>>(options, "audit-records.json");

    public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _store.UpdateAsync(static () => [], records => records.Add(record), cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).AsReadOnly();
}
