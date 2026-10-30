using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// Append-only JSON Lines audit store: one JSON object per line in <c>audit-records.jsonl</c>.
/// Each append writes only the new line at the end of the file, so the cost of an audited action does not grow
/// with the size of the log. Appends are serialized in this process; the file is never rewritten.
/// </summary>
public sealed class JsonAuditStore : IAuditStore
{
    public const string FileName = "audit-records.jsonl";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public JsonAuditStore(JsonRepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        _path = Path.GetFullPath(Path.Combine(options.DataDirectory, FileName));
        _gate = FileLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var line = Utf8NoBom.GetBytes(JsonSerializer.Serialize(record, SerializerOptions) + "\n");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new IOException("Audit file has no parent directory."));
            await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            // Once the write starts it is not cancelled, so a disconnecting client cannot leave half a line behind.
            await stream.WriteAsync(line, CancellationToken.None).ConfigureAwait(false);
            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return [];
            var records = new List<AuditRecord>();
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Utf8NoBom);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                records.Add(JsonSerializer.Deserialize<AuditRecord>(line, SerializerOptions)
                    ?? throw new JsonException($"Audit file '{_path}' contains a null record."));
            }
            return records.AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }
}
