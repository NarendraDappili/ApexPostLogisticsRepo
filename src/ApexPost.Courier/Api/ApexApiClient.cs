using System.Net;
using System.Net.Http.Json;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Api;

/// <summary>
/// The app's typed client for the v2 API, registered with AddHttpClient&lt;ApexApiClient&gt;(): the HttpClient is
/// made and recycled by IHttpClientFactory, with the base address from configuration and <see cref="AuthHeaderHandler"/>
/// in its pipeline. The app never creates an HttpClient itself.
/// </summary>
internal sealed class ApexApiClient(HttpClient http)
{
    /// <summary>Uploads one shipment's scans. Safe to send again: the server skips scan IDs it already has.</summary>
    public async Task<ScanUploadResult> UploadScansAsync(string trackingNumber, IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default)
    {
        var body = new ScanUploadRequest([.. scans.Select(ScanUploadItem.From)]);
        using var response = await http.PostAsJsonAsync(
            $"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}/scans", body, CourierJsonContext.Default.ScanUploadRequest, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync(CourierJsonContext.Default.ScanUploadResult, cancellationToken)
            ?? throw new ApexApiException(response.StatusCode, "The server sent an empty answer to the scan upload.");
    }

    /// <summary>Asks a Development API for a one-hour Courier token for this courier (POST dev/token).</summary>
    public async Task<DevelopmentToken> GetDevelopmentTokenAsync(string courierId, CancellationToken cancellationToken = default)
    {
        var body = new DevelopmentTokenRequest(courierId, ["Courier"], courierId);
        using var response = await http.PostAsJsonAsync("dev/token", body, CourierJsonContext.Default.DevelopmentTokenRequest, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync(CourierJsonContext.Default.DevelopmentToken, cancellationToken)
            ?? throw new ApexApiException(response.StatusCode, "The server sent an empty answer to the sign-in.");
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ApexApiException(response.StatusCode, $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }
}

/// <summary>The API answered, but not with success. <see cref="StatusCode"/> says how.</summary>
internal sealed class ApexApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
