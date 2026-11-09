using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>Small helpers for calling the API and reading its JSON, shared by the endpoint tests.</summary>
internal static class ApiClient
{
    public static object NewShipment(string trackingNumber, string city = "Portside", decimal weightKg = 5m) => new
    {
        trackingNumber,
        recipientName = "Ama Mensah",
        recipientPhone = "+44 20 7946 0000",
        recipientAddress = "12 Harbour Road",
        city,
        weightKg,
    };

    public static async Task<HttpResponseMessage> CreateAsync(this HttpClient client, string trackingNumber, string city = "Portside", decimal weightKg = 5m) =>
        await client.PostAsJsonAsync("/api/shipments", NewShipment(trackingNumber, city, weightKg));

    public static async Task<HttpResponseMessage> PatchStatusAsync(this HttpClient client, string trackingNumber, object status) =>
        await client.PatchAsJsonAsync($"/api/shipments/{trackingNumber}/status", new { status });

    /// <summary>Moves a new shipment along the chain to OutForDelivery, then optionally to a final status.</summary>
    public static async Task AdvanceAsync(this HttpClient client, string trackingNumber, int steps)
    {
        string[] chain = ["PickedUp", "InTransit", "OutForDelivery", "Delivered"];
        foreach (var status in chain.Take(steps))
        {
            using var response = await client.PatchStatusAsync(trackingNumber, status);
            response.EnsureSuccessStatusCode();
        }
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");
}
