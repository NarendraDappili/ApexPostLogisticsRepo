namespace ApexPost.Infrastructure.Persistence.StoredProcedures;

/// <summary>Reads the embedded CREATE OR ALTER scripts in this folder, so migrations deploy exactly the files in source control.</summary>
public static class StoredProcedureScripts
{
    public const string DailyBranchSales = "usp_DailyBranchSales";
    public const string LowStockReport = "usp_LowStockReport";
    public const string AdjustStock = "usp_AdjustStock";

    /// <summary>
    /// The script wrapped in EXEC(N'...'). CREATE PROCEDURE must start its own batch, and an idempotent migration script
    /// puts each migration inside an IF block, so the statement has to run as dynamic SQL there. Quotes are doubled.
    /// </summary>
    public static string ReadAsExec(string procedureName) => $"EXEC(N'{Read(procedureName).Replace("'", "''", StringComparison.Ordinal)}');";

    public static string Read(string procedureName)
    {
        var resource = $"{typeof(StoredProcedureScripts).Namespace}.{procedureName}.sql";
        using var stream = typeof(StoredProcedureScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The stored procedure script '{resource}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
