using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// Writes every stored audit record to the application log as well, under the <see cref="Category"/> category, so
/// the audit trail also reaches the console and any log sink. SQL Server stays the system of record. The log line
/// names the changed columns but never their values: those may be personal data and stay in the database.
/// </summary>
public sealed class AuditTrailLogger(ILoggerFactory loggerFactory)
{
    public const string Category = "ApexPost.Audit";
    private readonly ILogger _logger = loggerFactory.CreateLogger(Category);

    public void Request(AuditRecord record) =>
        _logger.LogInformation("Audit request {Controller}/{Action} {Outcome} {StatusCode} by {StaffCode} ({Role}) at {BranchCode} in {DurationMilliseconds}ms {CorrelationId}",
            record.Controller, record.Action, record.Outcome, record.StatusCode, record.StaffId ?? "anonymous", record.Role, record.BranchCode, record.DurationMilliseconds, record.CorrelationId);

    public void DataChange(DataChangeEntity change, IEnumerable<string> columns) =>
        _logger.LogInformation("Audit data change {Operation} {TableName} {RecordKey} columns [{Columns}] by {StaffCode} at {BranchCode} {CorrelationId}",
            change.Operation, change.TableName, change.RecordKey, string.Join(", ", columns), change.StaffId ?? "system", change.BranchCode, change.CorrelationId);

    public void Security(SecurityEventRecord record)
    {
        // Successful sign-ins and sign-outs are routine; refusals are what monitoring should notice.
        var level = record.EventType is SecurityEventType.SignInSucceeded or SecurityEventType.SignedOut ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(level, "Audit security {EventType} {StaffCode} {HttpMethod} {Path} {StatusCode} {Detail} from {RemoteIpAddress} {CorrelationId}",
            record.EventType, record.StaffId ?? "anonymous", record.HttpMethod, record.Path, record.StatusCode, record.Detail, record.RemoteIpAddress, record.CorrelationId);
    }
}
