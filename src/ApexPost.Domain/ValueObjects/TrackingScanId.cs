namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// The ID a courier's device gives a scan when it records it offline: <c>scan::</c> and a GUID, for example
/// <c>scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10</c>. The server keeps it, so an upload sent twice is recognised.
/// The GUID is stored in lower case, the way the device writes it.
/// </summary>
public sealed record TrackingScanId
{
    public const string Prefix = "scan::";
    public const int Length = 42;

    public TrackingScanId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        // Exact length first: Guid.TryParseExact would otherwise accept surrounding spaces.
        if (value.Length != Length || !value.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out var guid))
        {
            throw new ArgumentException("A scan ID must be scan:: followed by a GUID, for example scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10.", nameof(value));
        }

        Value = Prefix + guid.ToString("D");
    }

    public string Value { get; }

    public static bool TryParse(string? value, out TrackingScanId? scanId)
    {
        scanId = value is { Length: Length } && value.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out _)
            ? new TrackingScanId(value)
            : null;
        return scanId is not null;
    }

    public override string ToString() => Value;
}
