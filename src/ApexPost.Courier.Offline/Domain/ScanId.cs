using System.Diagnostics.CodeAnalysis;

namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Identifies a shipment scan document as <c>scan::{Guid}</c>. The ID is created on the device, so a scan keeps the
/// same identity from the first offline save to the server, which uses it to skip re-uploaded duplicates.
/// </summary>
public sealed record ScanId
{
    public const string Prefix = "scan::";
    private const int GuidLength = 36;

    private ScanId(Guid value)
    {
        Value = $"{Prefix}{value:D}";
    }

    public string Value { get; }

    /// <summary>
    /// Creates a new ID. A version 7 GUID starts with a timestamp, so IDs created later sort later.
    /// </summary>
    public static ScanId New() => new(Guid.CreateVersion7());

    public static ScanId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"A scan ID must look like {Prefix}{{Guid}}.");

    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out ScanId? id)
    {
        id = null;
        // The exact length matters: Guid parsing would accept surrounding spaces, but a document key with a trailing
        // space is a different document.
        if (value is null || value.Length != Prefix.Length + GuidLength || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out var guid) || guid == Guid.Empty) return false;

        id = new ScanId(guid);
        return true;
    }

    public override string ToString() => Value;
}
