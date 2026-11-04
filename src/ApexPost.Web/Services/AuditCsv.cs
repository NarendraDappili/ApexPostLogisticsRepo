using System.Globalization;
using System.Text;
using ApexPost.Application.Auditing;

namespace ApexPost.Web.Services;

/// <summary>
/// The audit log as CSV for a spreadsheet. Audit rows hold text that staff typed (names, reasons, paths), so a field
/// that a spreadsheet would run as a formula (starting with =, +, -, @, tab or carriage return) is prefixed with an
/// apostrophe, and every field is quoted.
/// </summary>
public static class AuditCsv
{
    public static string Requests(IEnumerable<RequestAuditItem> rows) => Write(
        ["OccurredAtUtc", "StaffCode", "Role", "BranchCode", "Controller", "Action", "RouteValues", "StatusCode", "DurationMs", "Outcome", "CorrelationId"],
        rows.Select(row => new[] { Time(row.OccurredAt), row.StaffId, row.Role, row.BranchCode, row.Controller, row.Action,
            string.Join("; ", row.RouteValues.Select(pair => $"{pair.Key}={pair.Value}")), Number(row.StatusCode), Number(row.DurationMilliseconds), row.Outcome, row.CorrelationId }));

    public static string Changes(IEnumerable<DataChangeItem> rows) => Write(
        ["OccurredAtUtc", "StaffCode", "BranchCode", "TableName", "RecordKey", "Operation", "Changes", "CorrelationId"],
        rows.Select(row => new[] { Time(row.OccurredAt), row.StaffId, row.BranchCode, row.TableName, row.RecordKey, row.Operation.ToString(),
            string.Join("; ", row.Columns.Select(column => $"{column.Column}: {column.Old ?? "(empty)"} -> {column.New ?? "(empty)"}")), row.CorrelationId }));

    public static string SecurityEvents(IEnumerable<SecurityEventItem> rows) => Write(
        ["OccurredAtUtc", "EventType", "StaffCode", "Role", "BranchCode", "HttpMethod", "Path", "StatusCode", "Detail", "RemoteIpAddress", "CorrelationId"],
        rows.Select(row => new[] { Time(row.OccurredAt), row.EventType.ToString(), row.StaffId, row.Role, row.BranchCode, row.HttpMethod, row.Path,
            Number(row.StatusCode), row.Detail, row.RemoteIpAddress, row.CorrelationId }));

    internal static string Field(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string Write(string[] header, IEnumerable<string?[]> rows)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', header.Select(Field)).Append("\r\n");
        foreach (var row in rows) csv.AppendJoin(',', row.Select(Field)).Append("\r\n");
        return csv.ToString();
    }

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
