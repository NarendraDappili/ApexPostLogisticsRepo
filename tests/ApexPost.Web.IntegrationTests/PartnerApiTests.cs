using System.Net;
using System.Text.Json;
using ApexPost.Application.Repositories;
using ApexPost.Application.Abstractions;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// The partner parcel-status API past its security checks: a valid key from an allowed address, a known or unknown
/// shipment, and a courier too slow to answer. The other tests of this endpoint cover only its refusals.
/// </summary>
public sealed class PartnerApiTests
{
    private const string ApiKey = "partner-test-key";

    [Fact]
    public async Task A_partner_with_a_valid_key_gets_the_stored_shipment_and_the_couriers_status()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await using var partner = Partner(factory, latencyMilliseconds: 0);
        await SeedShipmentAsync(partner, "SHP-PARTNER-1");
        using var client = partner.CreateClient();

        using var response = await client.SendAsync(Request("/integration/parcel-status?shipmentId=shp-partner-1"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("SHP-PARTNER-1", "BR-NORTH", "ZONE-01", "Standard", "Parcel"),
            (json.GetProperty("shipmentId").GetString(), json.GetProperty("originBranch").GetString(), json.GetProperty("destinationZone").GetString(),
             json.GetProperty("serviceLevel").GetString(), json.GetProperty("shipmentType").GetString()));
        Assert.False(json.GetProperty("courierLookupTimedOut").GetBoolean());
        Assert.NotEqual("Unavailable", json.GetProperty("courierStatus").GetString());
        // The partner request is in the request audit like any other, as an anonymous caller.
        Assert.Contains(await factory.GetAuditRecordsAsync(), record => record is { Controller: "Integration", Action: "ParcelStatus", StatusCode: 200, StaffId: null });
    }

    [Fact]
    public async Task A_slow_courier_degrades_the_answer_instead_of_holding_the_partner_request_open()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await using var partner = Partner(factory, latencyMilliseconds: 5_000, lookupTimeoutMilliseconds: 50);
        await SeedShipmentAsync(partner, "SHP-SLOW-1");
        using var client = partner.CreateClient();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        using var response = await client.SendAsync(Request("/integration/parcel-status?shipmentId=SHP-SLOW-1"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.RootElement.GetProperty("courierLookupTimedOut").GetBoolean());
        Assert.Equal("Unavailable", body.RootElement.GetProperty("courierStatus").GetString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"The request waited for the courier ({watch.Elapsed}).");
    }

    [Fact]
    public async Task An_unknown_shipment_is_404_and_a_missing_id_is_400()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await using var partner = Partner(factory, latencyMilliseconds: 0);
        using var client = partner.CreateClient();

        using var unknown = await client.SendAsync(Request("/integration/parcel-status?shipmentId=SHP-NEVER-SENT"));
        using var missing = await client.SendAsync(Request("/integration/parcel-status"));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    private static WebApplicationFactory<Program> Partner(AuthWebApplicationFactory factory, int latencyMilliseconds, int lookupTimeoutMilliseconds = 2_000) =>
        factory.WithWebHostBuilder(builder =>
        {
            // Program.cs reads these before Build(), which only UseSetting reaches.
            builder.UseSetting("ApexPost:Security:Integration:ApiKey", ApiKey);
            builder.UseSetting("ApexPost:Courier:LatencyMilliseconds", latencyMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("ApexPost:Courier:LookupTimeoutMilliseconds", lookupTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

    private static HttpRequestMessage Request(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Api-Key", ApiKey);
        return request;
    }

    private static async Task SeedShipmentAsync(WebApplicationFactory<Program> factory, string shipmentId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShipmentRepository>()
            .Add(new Shipment(new ShipmentId(shipmentId), new BranchCode("BR-NORTH"), new ZoneCode("ZONE-01"), new ParcelWeight(2m), new ParcelDimensions(10m, 10m, 10m)));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}
