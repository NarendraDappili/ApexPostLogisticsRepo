using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Api;

// The app's own copies of the v2 API's request and response bodies: only the fields the app sends or reads. They are
// written and read with CourierJsonContext, and a test compares them with contracts/v2.json.

/// <summary>The body of POST api/v2/shipments/{trackingNumber}/scans.</summary>
internal sealed record ScanUploadRequest(IReadOnlyList<ScanUploadItem> Scans);

/// <summary>One scan exactly as the device recorded it; the scan ID lets the server skip one it already has.</summary>
internal sealed record ScanUploadItem(string ScanId, ScanType ScanType, string Location, string CourierId, DateTimeOffset ScannedAtUtc, string? Notes)
{
    public static ScanUploadItem From(ShipmentScan scan) =>
        new(scan.Id.Value, scan.ScanType, scan.Location, scan.CourierId, scan.ScannedAtUtc, scan.Notes);
}

/// <summary>What the server did with an upload's scans.</summary>
/// <param name="ShipmentStatus">Kept as text: a status added to the API later must not stop the app reading the answer.</param>
internal sealed record ScanUploadResult(int Accepted, int DuplicatesSkipped, int Rejected, IReadOnlyList<ScanRejection>? Rejections, string? ShipmentStatus);

/// <summary>A scan the server refused, and why.</summary>
internal sealed record ScanRejection(string ScanId, string? Reason);

/// <summary>
/// The API's error body: ProblemDetails, or ValidationProblemDetails when <see cref="Errors"/> is present (RFC 9457).
/// Every field may be missing; extensions the app does not use, such as Development's exception details, are ignored.
/// </summary>
internal sealed record ApiProblem(string? Type, string? Title, int? Status, string? Detail, string? Instance, string? TraceId, Dictionary<string, string[]?>? Errors);

/// <summary>The body of PATCH api/v2/shipments/{trackingNumber}/status.</summary>
internal sealed record UpdateStatusRequest(string Status);

/// <summary>The body of the Development-only POST dev/token.</summary>
internal sealed record DevelopmentTokenRequest(string Name, IReadOnlyList<string> Roles, string CourierId);

/// <summary>A development access token and when it stops being accepted.</summary>
internal sealed record DevelopmentToken(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
