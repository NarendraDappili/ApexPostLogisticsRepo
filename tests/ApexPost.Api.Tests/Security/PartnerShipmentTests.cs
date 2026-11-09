using System.Net;
using System.Net.Http.Json;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D3 on a real database: with a valid key, a partner reads a shipment's status, city and latest scan, and nothing
/// about the recipient but the city. No bearer token is needed.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class PartnerShipmentTests(SqlApiFactory factory)
{
    [Theory]
    [InlineData(TestPartnerKeys.Northwind)]
    [InlineData(TestPartnerKeys.Contoso)]
    public async Task A_partner_with_a_valid_key_reads_the_shipment_without_the_recipients_details(string partner)
    {
        var number = SqlApiFactory.NextTrackingNumber();
        using (var staff = factory.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Created, (await staff.CreateAsync(number, city: "Portside")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/v2/shipments/{number}/scans", AuthorizationTests.Upload(TestTokens.CourierId))).StatusCode);
        }

        using var response = await PartnerClient(partner == TestPartnerKeys.Northwind ? TestPartnerKeys.NorthwindKey : TestPartnerKeys.ContosoKey)
            .GetAsync($"/api/v2/partner/shipments/{number}");
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["city", "latestScan", "status", "trackingNumber"], body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((number, "PickedUp", "Portside"), (body.GetProperty("trackingNumber").GetString(), body.GetProperty("status").GetString(), body.GetProperty("city").GetString()));
        // What happened and when; not where, because a delivery scan's location is the recipient's address.
        Assert.Equal(["scanType", "scannedAtUtc"], body.GetProperty("latestScan").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Pickup", body.GetProperty("latestScan").GetProperty("scanType").GetString());
    }

    [Fact]
    public async Task A_partner_gets_a_404_for_an_unknown_tracking_number()
    {
        using var response = await PartnerClient(TestPartnerKeys.NorthwindKey).GetAsync($"/api/v2/partner/shipments/{SqlApiFactory.NextTrackingNumber()}");
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("No shipment has the tracking number", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_partner_route_is_read_only()
    {
        // Signed in and with a key: whatever the credentials, no verb but GET is mapped.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PartnerApiKeyFilter.HeaderName, TestPartnerKeys.NorthwindKey);

        using var response = await client.PostAsJsonAsync("/api/v2/partner/shipments/APX-24000001", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private HttpClient PartnerClient(string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add(PartnerApiKeyFilter.HeaderName, key);
        return client;
    }
}
