using Microsoft.Data.SqlClient;

namespace ApexPost.TestSupport;

/// <summary>
/// The SQL Server the integration tests create their throwaway databases on, found with no manual set-up
/// (TESTING-STRATEGY.md §9.4). Linked into every project that needs SQL Server, so the rule lives in one place.
/// <list type="number">
/// <item><c>APEXPOST_TEST_SQLSERVER</c>, when set, used as given (CI sets it to its SQL Server container);</item>
/// <item>otherwise the local default instance, if it answers;</item>
/// <item>otherwise LocalDB, <c>(localdb)\MSSQLLocalDB</c>, which Visual Studio installs;</item>
/// <item>otherwise the run fails with a message saying what was tried. It never skips, because a skipped test reads
/// as a passed one.</item>
/// </list>
/// The search runs once per test process.
/// </summary>
internal static class TestSqlServer
{
    public const string Variable = "APEXPOST_TEST_SQLSERVER";

    // LocalDB starts its instance on the first connection, which can take a while.
    private static readonly (string Name, string ConnectionString, int TimeoutSeconds)[] Candidates =
    [
        ("the local default instance", "Server=.;Trusted_Connection=True;TrustServerCertificate=True", 5),
        ("LocalDB", @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True", 60),
    ];

    private static readonly Lazy<string> Server = new(Find, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The server's connection string, without a database name.</summary>
    public static string ConnectionString => Server.Value;

    /// <summary>The connection string for <paramref name="databaseName"/> on that server.</summary>
    public static string ForDatabase(string databaseName) =>
        new SqlConnectionStringBuilder(Server.Value) { InitialCatalog = databaseName }.ConnectionString;

    private static string Find()
    {
        var configured = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var tried = new List<string>();
        foreach (var (name, connectionString, timeoutSeconds) in Candidates)
        {
            var failure = TryConnect(connectionString, timeoutSeconds);
            if (failure is null) return connectionString;
            tried.Add($"{name}: {failure}");
        }

        throw new InvalidOperationException(
            "The integration tests need a SQL Server and none answered. Tried " + string.Join("; ", tried) + ". " +
            "Install SQL Server or LocalDB (Visual Studio's \"Data storage and processing\" workload includes LocalDB), " +
            $"or set {Variable} to a connection string without a database name.");
    }

    private static string? TryConnect(string connectionString, int timeoutSeconds)
    {
        try
        {
            var probe = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", ConnectTimeout = timeoutSeconds, Pooling = false };
            using var connection = new SqlConnection(probe.ConnectionString);
            connection.Open();
            return null;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or PlatformNotSupportedException)
        {
            return exception.Message.Split('\n')[0].Trim();
        }
    }
}
