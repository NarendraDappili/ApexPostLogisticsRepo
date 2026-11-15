using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApexPost.Api.Security;
using ApexPost.Api.IntegrationTests.Endpoints;
using ApexPost.Api.IntegrationTests.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ApexPost.Api.IntegrationTests.Security;

/// <summary>
/// D1: a Development API issues one-hour tokens from POST /dev/token, signed for the configured issuer and audience,
/// and accepts them on real endpoints of both versions.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class DevelopmentTokenTests(SqlApiFactory factory)
{
    [Fact]
    public async Task A_development_token_carries_its_roles_and_courier_id_and_expires_in_an_hour()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using var response = await client.PostAsJsonAsync(DevelopmentTokenEndpoint.Path, new { name = "Kofi Boateng", roles = new[] { "Courier" }, courierId = "CR-1042" });
        var body = await response.JsonAsync();
        var token = new JsonWebToken(body.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal((TestTokens.Issuer, TestTokens.Audience), (token.Issuer, Assert.Single(token.Audiences)));
        Assert.Equal(("Kofi Boateng", "Courier", "CR-1042"), (token.Subject, token.GetClaim("role").Value, token.GetClaim(ApiRoles.CourierIdClaim).Value));
        // Measured against the token's own start, not the clock; The_api_accepts_the_tokens_it_issued shows it is valid now.
        Assert.Equal(TimeSpan.FromHours(1), token.ValidTo - token.ValidFrom);
        Assert.Equal(token.ValidFrom, token.IssuedAt);
        Assert.Equal(token.ValidTo, body.GetProperty("expiresAtUtc").GetDateTimeOffset().UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("/api/v2/shipments")]
    [InlineData("/api/v1/shipments")]
    public async Task The_api_accepts_the_tokens_it_issued(string path)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var issued = await client.PostAsJsonAsync(DevelopmentTokenEndpoint.Path, new { name = "Back office", roles = new[] { "Admin" } });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await issued.JsonAsync()).GetProperty("accessToken").GetString());

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
