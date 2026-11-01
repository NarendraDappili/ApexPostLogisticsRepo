using ApexPost.Infrastructure.Persistence.Migrations;
using ApexPost.Infrastructure.Persistence.StoredProcedures;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ApexPost.Infrastructure.Tests.Persistence;

/// <summary>
/// Migrations are immutable snapshots: each one deploys the procedure text it shipped with, whatever the current
/// script in source control says. No database is needed; the operations are read from the migration classes.
/// </summary>
public sealed class StoredProcedureMigrationTests
{
    [Fact]
    public void The_first_migration_still_deploys_the_procedure_bodies_it_shipped()
    {
        var sql = string.Join('\n', Sql(new AddStoredProcedures()));

        // The original usp_DailyBranchSales used local variables, replaced later by RedeployDailyBranchSales.
        Assert.Contains("DECLARE @From datetime2(3)", sql, StringComparison.Ordinal);
        // The original usp_AdjustStock accepted any active staff member, tightened by AdjustStockRequiresBranchManager.
        Assert.Contains("The adjustment must be made by an active staff member.", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_latest_migration_for_each_procedure_deploys_the_current_script()
    {
        Assert.Equal(Exec(StoredProcedureScripts.DailyBranchSales), Assert.Single(Sql(new RedeployDailyBranchSales())));
        Assert.Equal(Exec(StoredProcedureScripts.AdjustStock), Assert.Single(Sql(new AdjustStockRequiresBranchManager())));
        Assert.Contains(Exec(StoredProcedureScripts.LowStockReport), Sql(new AddStoredProcedures()));
    }

    [Fact]
    public void Rolling_back_a_procedure_change_restores_the_body_shipped_before_it()
    {
        Assert.Equal(Sql(new AddStoredProcedures())[0], Assert.Single(Sql(new RedeployDailyBranchSales(), down: true)));
        Assert.Equal(Sql(new AddStoredProcedures())[2], Assert.Single(Sql(new AdjustStockRequiresBranchManager(), down: true)));
    }

    private static string Exec(string procedure) => Normalize(StoredProcedureScripts.AsExec(StoredProcedureScripts.Read(procedure).Trim()));

    private static string[] Sql(Migration migration, bool down = false) =>
        [.. (down ? migration.DownOperations : migration.UpOperations).OfType<SqlOperation>().Select(operation => Normalize(operation.Sql))];

    private static string Normalize(string sql) => sql.ReplaceLineEndings("\n");
}
