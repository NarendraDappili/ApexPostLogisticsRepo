using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ApexPost.Courier.Offline.Domain;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApexPost.Courier.Api;

/// <summary>
/// The app's typed client for the v2 API, registered with AddHttpClient&lt;ApexApiClient&gt;(): the HttpClient is
/// made and recycled by IHttpClientFactory, with the base address from configuration and the resilience pipeline and
/// <see cref="AuthHeaderHandler"/> in its handlers. The app never creates an HttpClient itself.
/// Every call either returns the answer or throws one of two exceptions, both with a message for the courier:
/// <see cref="ApexApiException"/> when the server answered with an error or something unreadable (its
/// ProblemDetails are kept), <see cref="ApiUnavailableException"/> when it could not be reached at all.
/// </summary>
internal sealed class ApexApiClient(HttpClient http)
{
    /// <summary>
    /// Uploads one shipment's scans. Marked safe to retry although it is a POST: every scan carries the ID the device
    /// gave it, and the server stores a scan ID only once (RESILIENCE.md).
    /// </summary>
    public async Task<ScanUploadResult> UploadScansAsync(string trackingNumber, IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default)
    {
        var body = new ScanUploadRequest([.. scans.Select(ScanUploadItem.From)]);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}/scans")
        {
            Content = JsonContent.Create(body, CourierJsonContext.Default.ScanUploadRequest),
        };
        request.Options.Set(ApiResilience.SafeToRetry, true);
        using var response = await SendAsync(request, cancellationToken);
        var result = await ReadAsync(response, CourierJsonContext.Default.ScanUploadResult, cancellationToken);
        // A missing or null list is an empty one; a null entry in it says nothing about any scan.
        return result with { Rejections = [.. (result.Rejections ?? []).Where(rejection => rejection is not null)] };
    }

    /// <summary>
    /// Asks a Development API for a one-hour Courier token for this courier (POST dev/token). Not retried: it is a
    /// POST the server does not de-duplicate, and the courier can simply sign in again.
    /// </summary>
    public async Task<DevelopmentToken> GetDevelopmentTokenAsync(string courierId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "dev/token")
        {
            Content = JsonContent.Create(new DevelopmentTokenRequest(courierId, ["Courier"], courierId), CourierJsonContext.Default.DevelopmentTokenRequest),
        };
        using var response = await SendAsync(request, cancellationToken);
        var token = await ReadAsync(response, CourierJsonContext.Default.DevelopmentToken, cancellationToken);
        return string.IsNullOrWhiteSpace(token.AccessToken) ? throw ApexApiException.Unreadable(response.StatusCode) : token;
    }

    /// <summary>Sends the request and returns a success response; anything else becomes one of the two exceptions.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled (the app is closing); that is not a server problem.
            throw;
        }
        catch (BrokenCircuitException exception)
        {
            throw new ApiUnavailableException(UnavailableReason.CircuitOpen, exception);
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or OperationCanceledException)
        {
            throw new ApiUnavailableException(UnavailableReason.TimedOut, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiUnavailableException(UnavailableReason.NoConnection, exception);
        }

        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            throw new ApexApiException(response.StatusCode, await ReadProblemAsync(response, cancellationToken));
        }
    }

    /// <summary>The ProblemDetails of an error response, or null when the body is empty or is not one (a proxy's HTML page).</summary>
    private static async Task<ApiProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize(text, CourierJsonContext.Default.ApiProblem);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Reads a success body; a missing, non-JSON or wrongly shaped one is an unreadable answer, never a crash.</summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(type, cancellationToken) ?? throw ApexApiException.Unreadable(response.StatusCode);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw ApexApiException.Unreadable(response.StatusCode, exception);
        }
    }
}
