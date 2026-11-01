namespace ApexPost.Infrastructure.Persistence.StoredProcedures;

/// <summary>
/// The CREATE OR ALTER scripts in this folder are the current version of each procedure, for review and for SSMS.
/// Migrations never read them: each migration keeps the exact text it deployed, so replaying it on a new database
/// always deploys the same body. A test checks that the migrated database runs exactly the scripts in this folder.
/// </summary>
public static class StoredProcedureScripts
{
    public const string DailyBranchSales = "usp_DailyBranchSales";
    public const string LowStockReport = "usp_LowStockReport";
    public const string AdjustStock = "usp_AdjustStock";

    /// <summary>
    /// The script wrapped in EXEC(N'...'). CREATE PROCEDURE must start its own batch, and an idempotent migration script
    /// puts each migration inside an IF block, so the statement has to run as dynamic SQL there. Quotes are doubled.
    /// </summary>
    public static string AsExec(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return $"EXEC(N'{script.Replace("'", "''", StringComparison.Ordinal)}');";
    }

    /// <summary>The current script of a procedure in this folder.</summary>
    public static string Read(string procedureName)
    {
        var resource = $"{typeof(StoredProcedureScripts).Namespace}.{procedureName}.sql";
        using var stream = typeof(StoredProcedureScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The stored procedure script '{resource}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
