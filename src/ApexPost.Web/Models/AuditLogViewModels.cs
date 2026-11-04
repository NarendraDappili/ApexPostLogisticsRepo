using ApexPost.Application.Auditing;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApexPost.Web.Models;

/// <summary>The three lists of the audit log screen.</summary>
public enum AuditLogTab
{
    Requests,
    Changes,
    Security
}

/// <summary>The audit log's filter form as it is submitted (GET); every field is optional.</summary>
public sealed class AuditLogQuery
{
    public string? BranchCode { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? StaffId { get; set; }
    public string? Kind { get; set; }
    public string? Result { get; set; }
    public string? CorrelationId { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>One audit log list with the filter that produced it and what the viewer's role allows.</summary>
public sealed record AuditLogViewModel(
    AuditLogTab Tab,
    AuditLogFilter Filter,
    DateOnly Today,
    bool CanViewAllBranches,
    bool CanViewSecurityEvents,
    IReadOnlyList<SelectListItem> KindOptions,
    IReadOnlyList<SelectListItem> ResultOptions,
    AuditLogPage<RequestAuditItem>? Requests = null,
    AuditLogPage<DataChangeItem>? Changes = null,
    AuditLogPage<SecurityEventItem>? SecurityEvents = null,
    string? Error = null)
{
    public int TotalCount => Requests?.TotalCount ?? Changes?.TotalCount ?? SecurityEvents?.TotalCount ?? 0;
    public int Page => Requests?.Page ?? Changes?.Page ?? SecurityEvents?.Page ?? 1;
    public int PageSize => Requests?.PageSize ?? Changes?.PageSize ?? SecurityEvents?.PageSize ?? AuditLogFilter.DefaultPageSize;
    public int TotalPages => Requests?.TotalPages ?? Changes?.TotalPages ?? SecurityEvents?.TotalPages ?? 1;

    /// <summary>The filter as route values, for the tab, page and export links.</summary>
    public Dictionary<string, string> RouteValues(int? page = null, bool keepKindAndResult = true)
    {
        var values = new Dictionary<string, string>
        {
            ["from"] = Filter.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["to"] = Filter.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (CanViewAllBranches && Filter.BranchCode is { } branch) values["branchCode"] = branch;
        if (Filter.StaffId is { } staff) values["staffId"] = staff;
        if (Filter.CorrelationId is { } correlation) values["correlationId"] = correlation;
        if (keepKindAndResult && Filter.Kind is { } kind) values["kind"] = kind;
        if (keepKindAndResult && Filter.Result is { } result) values["result"] = result;
        if (page is > 1) values["page"] = page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }
}

/// <summary>One request's trail across the three logs.</summary>
public sealed record AuditTraceViewModel(AuditTrace Trace, bool CanViewSecurityEvents);
