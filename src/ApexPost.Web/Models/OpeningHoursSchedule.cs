using System.Globalization;
using System.Text.RegularExpressions;

namespace ApexPost.Web.Models;

/// <summary>
/// Opening times picked on the branch form, for weekdays, Saturday and Sunday, and the opening-hours text the branch
/// stores ("Mon-Fri 08:30-17:30; Sat 09:00-12:30"). Days with the same times are written together ("Mon-Sat 08:00-20:00"),
/// as in the seeded branches. The text stays the branch's single source; this only builds and reads it.
/// </summary>
public sealed partial record OpeningHoursSchedule(OpeningTimes? Weekdays, OpeningTimes? Saturday, OpeningTimes? Sunday)
{
    public bool IsEmpty => Weekdays is null && Saturday is null && Sunday is null;

    /// <summary>The stored text, for example "Mon-Fri 09:00-17:30; Sat 09:00-12:30".</summary>
    public string ToText()
    {
        var segments = new List<string>();
        if (Weekdays is not null && Weekdays == Saturday && Weekdays == Sunday) segments.Add($"Mon-Sun {Weekdays}");
        else if (Weekdays is not null && Weekdays == Saturday)
        {
            segments.Add($"Mon-Sat {Weekdays}");
            if (Sunday is not null) segments.Add($"Sun {Sunday}");
        }
        else
        {
            if (Weekdays is not null) segments.Add($"Mon-Fri {Weekdays}");
            if (Saturday is not null && Saturday == Sunday) segments.Add($"Sat-Sun {Saturday}");
            else
            {
                if (Saturday is not null) segments.Add($"Sat {Saturday}");
                if (Sunday is not null) segments.Add($"Sun {Sunday}");
            }
        }
        return string.Join("; ", segments);
    }

    /// <summary>Reads text in the format <see cref="ToText"/> writes, or returns null for any other text.</summary>
    public static OpeningHoursSchedule? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        OpeningTimes? weekdays = null, saturday = null, sunday = null;
        foreach (var segment in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = SegmentPattern().Match(segment);
            if (!match.Success) return null;
            if (!TimeOnly.TryParseExact(match.Groups["opens"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
                || !TimeOnly.TryParseExact(match.Groups["closes"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes)
                || closes <= opens) return null;
            var times = new OpeningTimes(opens, closes);
            var days = match.Groups["days"].Value;
            if (days.StartsWith("Mon", StringComparison.Ordinal) && !Assign(ref weekdays, times)) return null;
            if (days is "Mon-Sat" or "Mon-Sun" or "Sat" or "Sat-Sun" && !Assign(ref saturday, times)) return null;
            if (days is "Mon-Sun" or "Sun" or "Sat-Sun" && !Assign(ref sunday, times)) return null;
        }
        return new OpeningHoursSchedule(weekdays, saturday, sunday);
    }

    // A day group given twice ("Sat 09:00-12:00; Sat 13:00-16:00") is not a format this schedule can show.
    private static bool Assign(ref OpeningTimes? slot, OpeningTimes times)
    {
        if (slot is not null) return false;
        slot = times;
        return true;
    }

    [GeneratedRegex(@"^(?<days>Mon-Fri|Mon-Sat|Mon-Sun|Sat-Sun|Sat|Sun) (?<opens>\d{2}:\d{2})-(?<closes>\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}

/// <summary>When a branch opens and closes on a day; it closes after it opens, on the same day.</summary>
public sealed record OpeningTimes(TimeOnly Opens, TimeOnly Closes)
{
    public override string ToString() =>
        $"{Opens.ToString("HH:mm", CultureInfo.InvariantCulture)}-{Closes.ToString("HH:mm", CultureInfo.InvariantCulture)}";
}
