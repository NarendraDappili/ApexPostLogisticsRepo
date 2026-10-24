using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace ApexPost.Web.IntegrationTests;

public sealed class MvcRoutesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Branch/Dashboard")]
    [InlineData("/Catalogue")]
    [InlineData("/CounterTransactions/New")]
    [InlineData("/Shipping/Create")]
    [InlineData("/MoneyOrders/Create")]
    [InlineData("/Reports")]
    public async Task Required_mvc_pages_return_success(string path)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_login_post_returns_validation_view()
    {
        using var client = CreateClient();
        var login = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(login, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Login", body);
    }

    private HttpClient CreateClient() => factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.ClearProviders()))
        .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
