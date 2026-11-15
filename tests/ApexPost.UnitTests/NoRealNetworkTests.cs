namespace ApexPost.UnitTests;

/// <summary>
/// Week 5 (S6): no test makes a real network call (TESTING-STRATEGY.md §7.4). Unit tests answer HTTP through a fake
/// <see cref="HttpMessageHandler"/>, and API and Web tests use <c>WebApplicationFactory</c>'s in-memory test server.
/// This guard reads the source of every test project and fails on any API that would open a real connection, so the
/// rule is enforced, not only hoped for.
/// </summary>
public sealed class NoRealNetworkTests
{
    [Theory]
    [InlineData("TcpListener")]
    [InlineData("TcpClient")]
    [InlineData("new Socket(")]
    [InlineData("UseUrls(")]
    [InlineData("UseKestrel(")]
    [InlineData("ListenAnyIP(")]
    [InlineData("ListenLocalhost(")]
    [InlineData("new HttpClient()")]
    [InlineData("new HttpClientHandler(")]
    [InlineData("new SocketsHttpHandler(")]
    [InlineData("Dns.GetHost")]
    public void TestSources_EveryTestProject_UsesNoApiThatOpensARealConnection(string forbidden)
    {
        // Act
        var offenders = TestSources()
            .Where(file => File.ReadAllText(file).Contains(forbidden, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryFiles.Root, file))
            .ToList();

        // Assert
        Assert.Empty(offenders);
    }

    [Fact]
    public void TestSources_TheScan_ReadsEveryTestProject()
    {
        // Act
        var projects = TestSources().Select(file => Path.GetRelativePath(RepositoryFiles.PathOf("tests"), file).Split(Path.DirectorySeparatorChar)[0]).ToHashSet();

        // Assert: the guard above would pass vacuously if it read nothing.
        Assert.Superset(
            new HashSet<string>
            {
                "ApexPost.UnitTests", "ApexPost.Api.IntegrationTests", "ApexPost.Web.IntegrationTests", "ApexPost.Infrastructure.IntegrationTests",
                "ApexPost.Courier.IntegrationTests", "ApexPost.Courier.Offline.IntegrationTests", "Shared",
            },
            projects);
    }

    /// <summary>Every C# file under tests/, except build output and this guard, which names the forbidden APIs.</summary>
    private static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(RepositoryFiles.PathOf("tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Path.GetFileName(file) != nameof(NoRealNetworkTests) + ".cs");
}
