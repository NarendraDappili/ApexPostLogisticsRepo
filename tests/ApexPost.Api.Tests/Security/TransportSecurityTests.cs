using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D5: HTTPS. Plain HTTP is redirected to HTTPS in every environment that knows its HTTPS port, permanently (308)
/// outside Development, before any token or key is looked at. Outside Development every HTTPS response carries HSTS.
/// Nothing in the source turns certificate validation off.
/// </summary>
public sealed partial class TransportSecurityTests
{
    // HSTS skips localhost by design, so these requests use a fictional public host name.
    private const string Host = "api.apexpost.example";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_https_responses_carry_hsts_for_a_year(string environment)
    {
        using var response = await SendAsync(environment, $"https://{Host}/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Development_sends_no_hsts()
    {
        using var response = await SendAsync("Development", $"https://{Host}/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("Production", HttpStatusCode.PermanentRedirect)]
    [InlineData("Development", HttpStatusCode.TemporaryRedirect)]
    public async Task Plain_http_is_redirected_to_https(string environment, HttpStatusCode expected)
    {
        using var response = await SendAsync(environment, $"http://{Host}/health");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(new Uri($"https://{Host}/health"), response.Headers.Location);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task An_api_request_over_plain_http_is_redirected_before_its_token_is_used()
    {
        // The default test token is valid; over HTTP it still gets no data, only the way to the https URL.
        using var response = await SendAsync("Production", $"http://{Host}/api/v2/shipments");

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
    }

    [Fact]
    public void No_source_file_turns_certificate_validation_off()
    {
        var offenders = Directory.EnumerateFiles(RepositoryFiles.PathOf("src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => CertificateBypass().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryFiles.Root, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator|RemoteCertificateValidationCallback")]
    private static partial Regex CertificateBypass();

    private static async Task<HttpResponseMessage> SendAsync(string environment, string url)
    {
        // https_port is what a deployment sets so that the redirect knows where to send the client.
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment(environment).UseSetting("https_port", "443"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{Host}") });
        var response = await client.GetAsync(new Uri(url));
        // Buffer the body before the host is disposed.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
