using ApexPost.Api.Security;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Client.Consumer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace ApexPost.Api.Tests.Client;

/// <summary>
/// D1: where the console consumer's bearer token comes from. A configured token is used as it is; otherwise a
/// Development API's /dev/token issues one, which is reused until shortly before it expires.
/// </summary>
public sealed class AccessTokensTests
{
    [Fact]
    public async Task A_configured_token_is_used_without_asking_the_api()
    {
        var calls = new CountingHandler();
        var tokens = new AccessTokens(Configuration(("ApexPostApi:AccessToken", "token-from-user-jwts")), new Clients(new HttpClient(calls)), TimeProvider.System);

        Assert.Equal("token-from-user-jwts", await tokens.GetAsync(CancellationToken.None));
        Assert.Equal(0, calls.Count);
    }

    [Fact]
    public async Task Otherwise_one_development_token_is_fetched_and_reused()
    {
        await using var factory = new OfflineApiFactory();
        var calls = new CountingHandler { InnerHandler = factory.Server.CreateHandler() };
        var tokens = new AccessTokens(Configuration(("ApexPostApi:CourierId", "CR-1042")), new Clients(new HttpClient(calls) { BaseAddress = factory.Server.BaseAddress }), TimeProvider.System);

        var first = await tokens.GetAsync(CancellationToken.None);
        var second = await tokens.GetAsync(CancellationToken.None);

        var token = new JsonWebToken(first);
        Assert.Equal(first, second);
        Assert.Equal(1, calls.Count);
        Assert.Equal(["Courier", "Admin"], token.GetPayloadValue<string[]>("role"));
        Assert.Equal("CR-1042", token.GetClaim(ApiRoles.CourierIdClaim).Value);
    }

    [Fact]
    public async Task Against_an_api_without_dev_token_the_error_says_how_to_pass_a_token()
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions());
        client.DefaultRequestHeaders.Authorization = null;
        var tokens = new AccessTokens(Configuration(("ApexPostApi:CourierId", "CR-1042")), new Clients(client), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => tokens.GetAsync(CancellationToken.None).AsTask());

        Assert.Contains("--ApexPostApi:AccessToken=<token>", exception.Message);
        Assert.Contains("401", exception.Message);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => KeyValuePair.Create(value.Key, (string?)value.Value))).Build();

    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler : DelegatingHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return InnerHandler is null ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)) : base.SendAsync(request, cancellationToken);
        }
    }
}
