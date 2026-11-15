using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.Drivers;

/// <summary>
/// How the steps talk to head office: the HTTP calls and their checks. The steps stay in business language and the
/// HTTP details live here, so a change to the API's shape changes one class, not the feature files.
/// </summary>
public sealed class HeadOfficeDriver(HeadOfficeContext headOffice) : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    /// <summary>An operations administrator: may register shipments (Admin).</summary>
    public HttpClient Administrator() => Client([ApiRoles.Admin]);

    /// <summary>A courier signed in to head office: may move a shipment along and upload scans (Courier).</summary>
    public HttpClient Courier(string courierId) => Client([ApiRoles.Courier], courierId);

    /// <summary>Head office's own operations, used to put a parcel where a scenario needs it.</summary>
    public HttpClient Operations() => Client([ApiRoles.Admin, ApiRoles.Courier], "CR-OPS");

    public string NextTrackingNumber() => headOffice.Host.NextTrackingNumber();

    public Task<HttpResponseMessage> RegisterAsync(HttpClient client, string trackingNumber, string recipientName = "Asha Rao", string city = "Pune", decimal weightKg = 2.5m) =>
        client.PostAsJsonAsync("api/v2/shipments", new
        {
            trackingNumber,
            recipientName,
            recipientPhone = "+44 20 7946 0000",
            recipientAddress = "14 Elm Street",
            city,
            weightKg,
        });

    /// <summary>Registers a parcel and moves it along the chain to <paramref name="status"/>, checking each answer.</summary>
    public async Task RegisterAtAsync(string trackingNumber, string status)
    {
        var operations = Operations();
        using var registered = await RegisterAsync(operations, trackingNumber);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        string[] chain = ["PickedUp", "InTransit", "OutForDelivery"];
        foreach (var step in chain.TakeWhile(_ => status != "Created"))
        {
            using var moved = await operations.PatchAsJsonAsync($"api/v2/shipments/{trackingNumber}/status", new { status = step });
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            if (step == status) break;
        }
    }

    /// <summary>The shipment as head office shows it, or null when head office does not know the number.</summary>
    public async Task<JsonElement?> LookUpAsync(string trackingNumber)
    {
        using var response = await Operations().GetAsync($"api/v2/shipments/{trackingNumber}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The scans head office has stored for the shipment, oldest first.</summary>
    public async Task<IReadOnlyList<JsonElement>> ScansAsync(string trackingNumber)
    {
        using var response = await Operations().GetAsync($"api/v2/shipments/{trackingNumber}/scans");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
    }

    private HttpClient Client(string[] roles, string? courierId = null)
    {
        var client = headOffice.Host.ClientFor(roles, courierId);
        _clients.Add(client);
        return client;
    }
}
