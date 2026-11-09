using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApexPost.Api.Tests.OpenApi;

/// <summary>
/// C2: Swagger UI at /swagger renders the built-in OpenAPI document, in Development only. Swashbuckle supplies the
/// viewer and nothing else: there is one generator, Microsoft.AspNetCore.OpenApi.
/// </summary>
public sealed partial class SwaggerUiTests
{
    [Fact]
    public async Task Development_serves_swagger_ui_with_the_api_title()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<title>ApexPost Logistics API</title>", html);
    }

    [Fact]
    public async Task The_swagger_path_redirects_to_the_ui_page()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/swagger");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("swagger/index.html", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_dropdown_lists_v2_first_then_v1_labelled_deprecated()
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        var script = await client.GetStringAsync("/swagger/index.js");
        var urls = JsonDocument.Parse(ConfigObject().Match(script).Groups["json"].Value).RootElement.GetProperty("urls");

        // The first entry is the one Swagger UI opens.
        Assert.Equal(
            [("v2", "/openapi/v2.json"), ("v1 (deprecated)", "/openapi/v1.json")],
            urls.EnumerateArray().Select(entry => (entry.GetProperty("name").GetString(), entry.GetProperty("url").GetString())));
    }

    [Theory]
    [InlineData("/openapi/v1.json", "1.0")]
    [InlineData("/openapi/v2.json", "2.0")]
    public async Task Each_dropdown_entry_loads_the_built_in_document_of_its_version(string url, string version)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();

        using var document = await client.GetAsync(url);
        var info = (await document.JsonAsync()).GetProperty("info");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Equal(("ApexPost Logistics API", version), (info.GetProperty("title").GetString(), info.GetProperty("version").GetString()));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_swagger_ui_is_not_served(string environment)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient();

        using var page = await client.GetAsync("/swagger/index.html");
        using var script = await client.GetAsync("/swagger/index.js");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, script.StatusCode);
    }

    [Fact]
    public void Swashbuckle_provides_the_viewer_only_never_a_second_generator()
    {
        // The test output holds every assembly the API depends on. SwaggerGen (and the Swagger middleware it feeds)
        // would be a second document generator next to Microsoft.AspNetCore.OpenApi.
        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(Path.GetFileNameWithoutExtension).ToList();

        Assert.Contains("Microsoft.AspNetCore.OpenApi", assemblies);
        Assert.Contains("Swashbuckle.AspNetCore.SwaggerUI", assemblies);
        Assert.DoesNotContain("Swashbuckle.AspNetCore.SwaggerGen", assemblies);
        Assert.DoesNotContain("Swashbuckle.AspNetCore.Swagger", assemblies);
    }

    [GeneratedRegex(@"var configObject = (?<json>\{.*?\});")]
    private static partial Regex ConfigObject();
}
