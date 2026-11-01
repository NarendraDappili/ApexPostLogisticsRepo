using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// Captures, with ToQueryString, the SQL that EF Core generates for three important screen and report queries. The
/// text is printed to the test output and copied into PERFORMANCE.md; the assertions pin the parts the indexes
/// depend on (equality on the branch first, a half-open date range, no function on a filtered column).
/// Regenerate with: dotnet test tests/ApexPost.Infrastructure.Tests --filter GeneratedSqlTests --logger "console;verbosity=detailed"
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GeneratedSqlTests(SqlServerTestDatabase database, ITestOutputHelper output)
{
    private static readonly BranchCode Branch = new("BR-0001");

    [Fact]
    public async Task The_dashboard_is_one_statement_with_sargable_branch_and_date_filters()
    {
        await using var db = database.CreateContext();

        var sql = Capture(new EfBranchQueries(db).DashboardQuery(Branch, new DateOnly(2026, 10, 15), lowStockThreshold: 10).ToQueryString());

        Assert.Equal(1, Count(sql, "FROM [Branches]"));
        Assert.Contains("[c0].[Status] = 'Paid'", sql, StringComparison.Ordinal);
        Assert.Matches(@"\[c0\]\.\[OccurredAtUtc\] >= @\w+ AND \[c0\]\.\[OccurredAtUtc\] < @\w+", sql);
        Assert.Matches(@"\[b0\]\.\[Quantity\] <= @\w+", sql);
    }

    [Fact]
    public async Task The_shipping_volume_report_groups_one_branch_by_zone_and_service_level_in_sql()
    {
        await using var db = database.CreateContext();

        var sql = Capture(new EfBranchReportQueries(db).ShippingVolumeQuery(Branch).ToQueryString());

        Assert.Matches(@"WHERE \[s\]\.\[OriginBranchCode\] = @\w+", sql);
        Assert.Contains("GROUP BY [s].[ZoneCode], [s].[ServiceLevel]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_money_order_report_left_joins_the_verification_and_groups_by_outcome_in_sql()
    {
        await using var db = database.CreateContext();

        var sql = Capture(new EfBranchReportQueries(db).MoneyOrderOutcomesQuery(Branch).ToQueryString());

        Assert.Contains("LEFT JOIN [IdentityVerifications]", sql, StringComparison.Ordinal);
        Assert.Matches(@"WHERE \[m\]\.\[BranchCode\] = @\w+", sql);
        Assert.Contains("GROUP BY", sql, StringComparison.Ordinal);
    }

    private string Capture(string sql)
    {
        output.WriteLine(sql);
        return sql;
    }

    private static int Count(string text, string value) => text.Split(value).Length - 1;
}
