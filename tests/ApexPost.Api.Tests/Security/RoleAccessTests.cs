using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D2, the other side of <see cref="AuthorizationTests"/>: with the right role each action does its work on a real
/// database. Reads need no particular role, and a courier's own scans are stored.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class RoleAccessTests(SqlApiFactory factory)
{
    [Theory]
    [InlineData(ApiRoles.Courier)]
    [InlineData(ApiRoles.Admin)]
    public async Task Any_valid_token_can_read(string role)
    {
        var number = await NewShipmentAsync();
        using var client = ClientWith(TestTokens.Create([role], courierId: role == ApiRoles.Courier ? TestTokens.CourierId : null));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v2/shipments/{number}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v2/shipments?pageSize=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/shipments/{number}/scans")).StatusCode);
    }

    [Fact]
    public async Task An_admin_creates_replaces_and_deletes_shipments()
    {
        using var admin = ClientWith(TestTokens.Create([ApiRoles.Admin], courierId: null));
        var number = SqlApiFactory.NextTrackingNumber();

        Assert.Equal(HttpStatusCode.Created, (await admin.CreateAsync(number)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v2/shipments/{number}", ApiClient.NewShipment(number, weightKg: 6m))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v2/shipments/{number}")).StatusCode);
    }

    [Fact]
    public async Task A_courier_moves_a_shipment_and_uploads_scans_under_their_own_courier_id()
    {
        var number = await NewShipmentAsync();
        using var courier = ClientWith(TestTokens.Create([ApiRoles.Courier]));

        using var patched = await courier.PatchStatusAsync(number, "PickedUp");
        using var uploaded = await courier.PostAsync($"/api/v2/shipments/{number}/scans", AuthorizationTests.Upload(TestTokens.CourierId, TestTokens.CourierId));
        var result = await uploaded.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.Equal(2, result.GetProperty("accepted").GetInt32() + result.GetProperty("duplicatesSkipped").GetInt32() + result.GetProperty("rejected").GetInt32());
        var history = await (await courier.GetAsync($"/api/v2/shipments/{number}/scans")).JsonAsync();
        Assert.All(history.EnumerateArray(), scan => Assert.Equal(TestTokens.CourierId, scan.GetProperty("courierId").GetString()));
    }

    [Fact]
    public async Task A_refused_upload_stores_nothing()
    {
        var number = await NewShipmentAsync();
        using var courier = ClientWith(TestTokens.Create([ApiRoles.Courier]));

        using var refused = await courier.PostAsync($"/api/v2/shipments/{number}/scans", AuthorizationTests.Upload(TestTokens.CourierId, "CR-2001"));
        var history = await (await courier.GetAsync($"/api/v2/shipments/{number}/scans")).JsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(0, history.GetArrayLength());
    }

    private async Task<string> NewShipmentAsync()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        using var client = factory.CreateClient();
        using var created = await client.CreateAsync(number);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return number;
    }

    private HttpClient ClientWith(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
