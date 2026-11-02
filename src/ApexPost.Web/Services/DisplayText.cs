using System.Text.RegularExpressions;

namespace ApexPost.Web.Services;

/// <summary>Readable labels for enum values shown to staff: CounterClerk becomes "Counter Clerk".</summary>
public static partial class DisplayText
{
    public static string Of(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return WordBoundary().Replace(value.ToString(), " ");
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
