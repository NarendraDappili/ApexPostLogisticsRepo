using System.Text.RegularExpressions;

namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// The public number of a tracked shipment: <c>APX-</c> and eight digits, for example <c>APX-24000001</c>. Input is
/// trimmed and upper-cased. Only ASCII digits count: <c>\d</c> would also accept other scripts' digits.
/// </summary>
public sealed partial record TrackingNumber
{
    public const int Length = 12;

    public TrackingNumber(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (!Format().IsMatch(normalized))
        {
            throw new ArgumentException("A tracking number must be APX- followed by eight digits, for example APX-24000001.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out TrackingNumber? trackingNumber)
    {
        trackingNumber = !string.IsNullOrWhiteSpace(value) && Format().IsMatch(value.Trim().ToUpperInvariant()) ? new TrackingNumber(value) : null;
        return trackingNumber is not null;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^APX-[0-9]{8}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
