using ApexPost.Application.Abstractions;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// The DataChanges and SecurityEvents tables as the AddAuditTrail migration creates them: valid rows are accepted and
/// stamped like every other table, and the CHECK constraints reject what the application must never write.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditTrailSchemaTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task A_data_change_and_a_security_event_are_stored_and_stamped_with_the_signed_in_staff()
    {
        var correlation = Correlation();
        await using (var db = database.CreateContext(new FixedStaff(new StaffId("STF-AUDITOR-01"))))
        {
            db.DataChanges.Add(Change(correlation));
            db.SecurityEvents.Add(Event(correlation));
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var change = await check.DataChanges.AsNoTracking().SingleAsync(row => row.CorrelationId == correlation);
        var securityEvent = await check.SecurityEvents.AsNoTracking().SingleAsync(row => row.CorrelationId == correlation);
        Assert.Equal(("Staff", "StaffCode=STF-CLERK-01", "Update"), (change.TableName, change.RecordKey, change.Operation));
        Assert.Equal(("SignInFailed", "STF-NOBODY", 200), (securityEvent.EventType, securityEvent.StaffId, securityEvent.StatusCode));
        Assert.Equal("STF-AUDITOR-01", await check.Database.SqlQuery<string>($"SELECT CreatedBy AS [Value] FROM DataChanges WHERE CorrelationId = {correlation}").SingleAsync());
        Assert.Equal("STF-AUDITOR-01", await check.Database.SqlQuery<string>($"SELECT CreatedBy AS [Value] FROM SecurityEvents WHERE CorrelationId = {correlation}").SingleAsync());
    }

    [Fact]
    public async Task The_data_change_checks_reject_an_unknown_operation_and_changes_that_are_not_json()
    {
        await AssertRejectedAsync(db => db.DataChanges.Add(Change(Correlation(), operation: "Truncate")));
        await AssertRejectedAsync(db => db.DataChanges.Add(Change(Correlation(), changesJson: "Name was changed")));
    }

    [Fact]
    public async Task The_security_event_checks_reject_an_unknown_event_type_and_an_impossible_status()
    {
        await AssertRejectedAsync(db => db.SecurityEvents.Add(Event(Correlation(), eventType: "PasswordShown")));
        await AssertRejectedAsync(db => db.SecurityEvents.Add(Event(Correlation(), statusCode: 700)));
    }

    private async Task AssertRejectedAsync(Action<ApexPostDbContext> add)
    {
        await using var db = database.CreateContext();
        add(db);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CHECK constraint", exception.InnerException?.Message, StringComparison.Ordinal);
    }

    private static string Correlation() => $"corr-{Guid.NewGuid():N}";

    private static DataChangeEntity Change(string correlationId, string operation = "Update", string changesJson = """[{"column":"Name","old":"Clerk","new":"Renamed Clerk"}]""") => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow,
        StaffId = "STF-ADMIN-01",
        BranchCode = "BR-NORTH",
        TableName = "Staff",
        RecordKey = "StaffCode=STF-CLERK-01",
        Operation = operation,
        ChangesJson = changesJson,
        CorrelationId = correlationId,
    };

    private static SecurityEventEntity Event(string correlationId, string eventType = "SignInFailed", int statusCode = 200) => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow,
        EventType = eventType,
        StaffId = "STF-NOBODY",
        HttpMethod = "POST",
        Path = "/Account/Login",
        StatusCode = statusCode,
        RemoteIpAddress = "127.0.0.1",
        Detail = "UnknownStaff",
        CorrelationId = correlationId,
    };

    private sealed class FixedStaff(StaffId staffId) : ICurrentStaffContext
    {
        public StaffId? StaffId => staffId;
        public BranchCode? BranchCode => null;
    }
}
