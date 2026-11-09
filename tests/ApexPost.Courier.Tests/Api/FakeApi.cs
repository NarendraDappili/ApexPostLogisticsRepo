using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// A small in-memory stand-in for the v2 API behind the app's HttpClient, so app tests never reach a real server.
/// It follows the real API's rules where the app depends on them (API-NOTES.md, SECURITY.md):
/// <list type="bullet">
/// <item>POST dev/token issues a token for the courier ID sent; a scan upload needs it (401 otherwise);</item>
/// <item>a courier uploads only under their own courierId (403);</item>
/// <item>a scan more than 5 minutes in the future is a 400 naming scans[i].scannedAtUtc;</item>
/// <item>an unknown tracking number is a 404;</item>
/// <item>a scan ID is stored once: sent again, it counts as a duplicate.</item>
/// </list>
/// </summary>
internal sealed partial class FakeApi
{
    private readonly ConcurrentDictionary<string, string> _stored = new(StringComparer.Ordinal);

    public FakeApi()
    {
        Server.Respond = Answer;
    }

    public StubServer Server { get; } = new();

    /// <summary>When set, every request gets 503, as from a server that is down.</summary>
    public bool Down { get; set; }

    /// <summary>Tracking numbers the API does not know (404).</summary>
    public HashSet<string> UnknownShipments { get; } = [];

    /// <summary>A server-side rule: a reason to reject the scan (its JSON), or null.</summary>
    public Func<JsonNode, string?>? RejectWhen { get; set; }

    /// <summary>The scan IDs the server has stored, with their tracking numbers.</summary>
    public IReadOnlyDictionary<string, string> Stored => _stored;

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        if (Down) return Problem(HttpStatusCode.ServiceUnavailable, "Service Unavailable", null);

        var body = request.Content is null ? null : JsonNode.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/dev/token", StringComparison.Ordinal))
        {
            var courierId = body!["courierId"]!.GetValue<string>();
            return StubServer.Json(HttpStatusCode.OK, $$"""{ "accessToken": "fake-token-{{courierId}}", "tokenType": "Bearer", "expiresAtUtc": "{{DateTimeOffset.UtcNow.AddHours(1):O}}" }""");
        }

        var upload = UploadPath().Match(path);
        if (!upload.Success) return Problem(HttpStatusCode.NotFound, "Not Found", null);

        var auth = request.Headers.Authorization?.Parameter;
        if (auth is null || !auth.StartsWith("fake-token-", StringComparison.Ordinal))
        {
            return Problem(HttpStatusCode.Unauthorized, "Authentication is required.", "Send an access token in the Authorization header: Authorization: Bearer <token>.");
        }

        var courier = auth["fake-token-".Length..];
        var trackingNumber = upload.Groups["tn"].Value;
        var scans = body!["scans"]!.AsArray().Select(scan => scan!).ToList();
        var others = scans.Select(scan => scan["courierId"]!.GetValue<string>()).Where(id => id != courier).Distinct().ToList();
        if (others.Count > 0)
        {
            return Problem(HttpStatusCode.Forbidden, "Access is denied.", $"A courier can upload scans only under their own courierId, {courier}. This upload has scans under {string.Join(", ", others)}.");
        }

        var errors = new JsonObject();
        for (var i = 0; i < scans.Count; i++)
        {
            if (DateTimeOffset.Parse(scans[i]["scannedAtUtc"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind) > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                errors[$"scans[{i}].scannedAtUtc"] = new JsonArray("scannedAtUtc cannot be in the future; it is more than 5 minutes ahead of the server clock.");
            }
        }

        if (errors.Count > 0)
        {
            return StubServer.Json(HttpStatusCode.BadRequest, new JsonObject { ["title"] = "One or more validation errors occurred.", ["status"] = 400, ["errors"] = errors }.ToJsonString(), "application/problem+json");
        }

        if (UnknownShipments.Contains(trackingNumber)) return Problem(HttpStatusCode.NotFound, "The requested record was not found.", $"No shipment has the tracking number {trackingNumber}.");

        int accepted = 0, duplicates = 0;
        var rejections = new JsonArray();
        foreach (var scan in scans)
        {
            var id = scan["scanId"]!.GetValue<string>();
            if (RejectWhen?.Invoke(scan) is { } reason) rejections.Add(new JsonObject { ["scanId"] = id, ["reason"] = reason });
            else if (_stored.TryAdd(id, trackingNumber)) accepted++;
            else duplicates++;
        }

        return StubServer.Json(HttpStatusCode.OK, new JsonObject
        {
            ["accepted"] = accepted,
            ["duplicatesSkipped"] = duplicates,
            ["rejected"] = rejections.Count,
            ["rejections"] = rejections,
            ["shipmentStatus"] = "PickedUp",
        }.ToJsonString());
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string? detail) =>
        StubServer.Json(status, new JsonObject { ["title"] = title, ["status"] = (int)status, ["detail"] = detail, ["traceId"] = "00-fake-trace-00" }.ToJsonString(), "application/problem+json");

    [GeneratedRegex(@"/api/v2/shipments/(?<tn>APX-\d{8})/scans$")]
    private static partial Regex UploadPath();
}
