namespace ApexPost.Api.Versioning;

/// <summary>
/// The API's versions and v1's retirement plan (VERSIONING.md). The version is the URL's first segment after /api:
/// /api/v1/shipments, /api/v2/shipments.
/// </summary>
internal static class ApiVersions
{
    /// <summary>The Part B contract, unchanged. Deprecated since v2 was released.</summary>
    public const double V1 = 1.0;

    /// <summary>Nested recipient, latest scan, and every enum written by name.</summary>
    public const double V2 = 2.0;

    /// <summary>When v1 was deprecated: the day v2 was released. Responses from v1 carry a Deprecation header.</summary>
    public static readonly DateTimeOffset V1DeprecatedOn = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    /// <summary>After this date v1 (and the unversioned Part B URLs) may stop answering. Responses from v1 carry a Sunset header.</summary>
    public static readonly DateTimeOffset V1SunsetOn = new(2027, 5, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The page that explains the versions and the move to v2; linked from v1's Deprecation and Sunset headers.</summary>
    public const string PolicyUrl = "https://developer.apexpost.example/api/versioning";
}
