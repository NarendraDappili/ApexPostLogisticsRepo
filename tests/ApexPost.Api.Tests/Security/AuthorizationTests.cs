using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D2: roles and the courierId rule. Scan upload and status changes need the Courier role; creating, replacing and
/// deleting shipments need Admin; reading needs any valid token. A token without the role is a 403 ProblemDetails,
/// and so is an upload with a scan under another courier's ID. None of these refusals reaches the database.
/// </summary>
public sealed class AuthorizationTests
{
    private static readonly string CourierOnly = TestTokens.Create([ApiRoles.Courier]);
    private static readonly string AdminOnly = TestTokens.Create([ApiRoles.Admin], courierId: null);

    public static TheoryData<string, string, string, string> WrongRole => new()
    {
        { "courier", "POST", "/api/v2/shipments", ApiRoles.Admin },
        { "courier", "PUT", "/api/v2/shipments/APX-24000001", ApiRoles.Admin },
        { "courier", "DELETE", "/api/v2/shipments/APX-24000001", ApiRoles.Admin },
        { "courier", "DELETE", "/api/v1/shipments/APX-24000001", ApiRoles.Admin },
        { "admin", "PATCH", "/api/v2/shipments/APX-24000001/status", ApiRoles.Courier },
        { "admin", "PATCH", "/api/v1/shipments/APX-24000001/status", ApiRoles.Courier },
        { "admin", "POST", "/api/v2/shipments/APX-24000001/scans", ApiRoles.Courier },
        { "admin", "POST", "/api/v1/shipments/APX-24000001/scans", ApiRoles.Courier },
        // The unversioned Part B alias is v1, with v1's rules.
        { "courier", "DELETE", "/api/shipments/APX-24000001", ApiRoles.Admin },
    };

    [Theory]
    [MemberData(nameof(WrongRole))]
    public async Task A_token_without_the_role_an_action_needs_is_a_403_problem(string who, string method, string path, string role)
    {
        // The body is irrelevant: roles are checked before it is even read.
        using var response = await SendAsync(who == "admin" ? AdminOnly : CourierOnly, method, path, ApiClient.RawJson("{}"));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal((AuthenticationRegistration.ForbiddenTitle, 403), (problem.GetProperty("title").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.Equal($"This action needs the {role} role, which the access token does not have.", problem.GetProperty("detail").GetString());
        // The alias is rewritten to v1 before routing, so its errors name the v1 path, as every other alias error does.
        Assert.Equal($"{method} {path.Replace("/api/shipments", "/api/v1/shipments", StringComparison.Ordinal)}", problem.GetProperty("instance").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("v2")]
    [InlineData("v1")]
    public async Task A_courier_cannot_upload_scans_under_another_courier_id(string version)
    {
        using var response = await SendAsync(CourierOnly, "POST", $"/api/{version}/shipments/APX-99999999/scans", Upload(TestTokens.CourierId, "CR-2001", "CR-2001"));
        var problem = await response.JsonAsync();

        // A 403 even though the shipment does not exist: who may upload is decided before the shipment is looked up.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(AuthenticationRegistration.ForbiddenTitle, problem.GetProperty("title").GetString());
        Assert.Equal("A courier can upload scans only under their own courierId, CR-1042. This upload has scans under CR-2001.", problem.GetProperty("detail").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Courier_ids_are_compared_exactly()
    {
        using var response = await SendAsync(CourierOnly, "POST", "/api/v2/shipments/APX-99999999/scans", Upload("cr-1042"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_courier_token_without_a_courier_id_cannot_upload_anything()
    {
        var withoutId = TestTokens.Create([ApiRoles.Courier], courierId: null);

        using var response = await SendAsync(withoutId, "POST", "/api/v2/shipments/APX-99999999/scans", Upload(TestTokens.CourierId));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("The access token has no courierId claim, so it cannot upload scans.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_malformed_upload_is_a_400_before_its_courier_ids_are_checked()
    {
        using var response = await SendAsync(CourierOnly, "POST", "/api/v2/shipments/APX-99999999/scans",
            JsonContent.Create(new { scans = new[] { new { scanId = "not-a-scan-id", scanType = "Pickup", location = "Depot 4", courierId = "CR-2001", scannedAtUtc = DateTimeOffset.UtcNow } } }));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("scans[0].scanId", out _));
    }

    /// <summary>The rule for each action, the same in both versions; null is any valid token.</summary>
    private static readonly Dictionary<string, string?> RoleByAction = new(StringComparer.Ordinal)
    {
        ["ListShipments"] = null,
        ["GetShipment"] = null,
        ["CreateShipment"] = ApiRoles.Admin,
        ["UpdateShipment"] = ApiRoles.Admin,
        ["UpdateShipmentStatus"] = ApiRoles.Courier,
        ["DeleteShipment"] = ApiRoles.Admin,
        ["GetScanHistory"] = null,
        ["UploadScans"] = ApiRoles.Courier,
    };

    [Fact]
    public void Every_action_in_both_versions_has_its_role_and_declares_a_403_exactly_when_it_has_one()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ControllerBase)))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .ToList();

        Assert.Equal(RoleByAction.Count * 2, actions.Count);
        Assert.All(actions, action =>
        {
            var role = RoleByAction[action.Name];
            var declares403 = action.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(produces => produces.StatusCode == 403);
            Assert.Equal(role, action.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            Assert.True(declares403 == (role is not null), $"{action.DeclaringType!.FullName}.{action.Name}");
            Assert.Equal(action.Name == "UploadScans", action.GetCustomAttribute<RequireOwnCourierIdAttribute>() is not null);
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        });
    }

    internal static JsonContent Upload(params string[] courierIds) => JsonContent.Create(new
    {
        scans = courierIds.Select(courierId => new
        {
            scanId = "scan::" + Guid.CreateVersion7(),
            scanType = "Pickup",
            location = "Depot 4",
            courierId,
            scannedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        }),
    });

    private static async Task<HttpResponseMessage> SendAsync(string token, string method, string path, HttpContent content)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method is "GET" or "DELETE" ? null : content };
        var response = await client.SendAsync(request);
        // Buffer the body before the host is disposed.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
