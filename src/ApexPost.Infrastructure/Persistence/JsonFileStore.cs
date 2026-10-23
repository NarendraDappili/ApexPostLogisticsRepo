using System.Collections.Concurrent;
using System.Text.Json;

namespace ApexPost.Infrastructure.Persistence;

internal sealed class JsonFileStore<TDocument>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public JsonFileStore(JsonRepositoryOptions options, string fileName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _path = Path.GetFullPath(Path.Combine(options.DataDirectory, fileName));
        _gate = FileLocks.GetOrAdd(_path, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<TDocument> ReadAsync(Func<TDocument> emptyDocument, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emptyDocument);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(emptyDocument, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        Func<TDocument> emptyDocument,
        Action<TDocument> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emptyDocument);
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadCoreAsync(emptyDocument, cancellationToken).ConfigureAwait(false);
            update(document);
            await WriteCoreAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> UpdateAsync<TResult>(
        Func<TDocument> emptyDocument,
        Func<TDocument, TResult> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emptyDocument);
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadCoreAsync(emptyDocument, cancellationToken).ConfigureAwait(false);
            var result = update(document);
            await WriteCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TDocument> ReadCoreAsync(Func<TDocument> emptyDocument, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
        {
            return emptyDocument();
        }

        await using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            throw new JsonException($"JSON data file '{_path}' is empty.");
        }

        var document = await JsonSerializer.DeserializeAsync<TDocument>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return document ?? throw new JsonException($"JSON data file '{_path}' contains a null document.");
    }

    private async Task WriteCoreAsync(TDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_path) ?? throw new IOException("Data file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
