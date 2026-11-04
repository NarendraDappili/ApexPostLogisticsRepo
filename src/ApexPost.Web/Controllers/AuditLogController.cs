using System.Security.Claims;
using System.Text;
using ApexPost.Application.Auditing;
using ApexPost.Domain.Enums;
using ApexPost.Web.Filters;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using ApexPost.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApexPost.Web.Controllers;

/// <summary>
/// The read-only audit log: the request audit, the data history and the security events, with filters, paging, a
/// per-request trace and CSV export. Auditors and Administrators read every branch and every list; a Branch Manager
/// reads their own branch's requests and data changes (SameBranchOnly refuses another branch in the URL, and
/// <see cref="AuditLogService"/> applies the same rule again). Counter Clerks have no access.
/// </summary>
[Authorize(Roles = "Auditor,Administrator,BranchManager", Policy = AuthorizationPolicies.SameBranchOnly)]
public sealed class AuditLogController(AuditLogService auditLog, TimeProvider timeProvider, IActionDescriptorCollectionProvider actions) : Controller
{
    public const int ExportLimit = 5000;

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(Requests));

    [HttpGet, HandlesInvalidModel]
    public Task<IActionResult> Requests(AuditLogQuery query, CancellationToken cancellationToken) => ShowAsync(AuditLogTab.Requests, query, cancellationToken);

    [HttpGet, HandlesInvalidModel]
    public Task<IActionResult> Changes(AuditLogQuery query, CancellationToken cancellationToken) => ShowAsync(AuditLogTab.Changes, query, cancellationToken);

    [HttpGet, HandlesInvalidModel, Authorize(Roles = "Auditor,Administrator")]
    public Task<IActionResult> Security(AuditLogQuery query, CancellationToken cancellationToken) => ShowAsync(AuditLogTab.Security, query, cancellationToken);

    /// <summary>Everything one request left in the three logs.</summary>
    [HttpGet]
    public async Task<IActionResult> Trace(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return BadRequest();
        if (Viewer() is not { } viewer) return Forbid();
        try
        {
            var trace = await auditLog.GetTraceAsync(viewer, id, cancellationToken);
            return View(new AuditTraceViewModel(trace, viewer.CanViewSecurityEvents));
        }
        catch (AuditLogAccessDeniedException) { return Forbid(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
    }

    /// <summary>The filtered list as CSV, newest first, at most <see cref="ExportLimit"/> rows.</summary>
    [HttpGet, HandlesInvalidModel]
    public async Task<IActionResult> Export(AuditLogTab tab, AuditLogQuery query, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(tab)) return BadRequest();
        if (Viewer() is not { } viewer) return Forbid();
        var filter = Filter(query) with { PageSize = AuditLogFilter.MaxPageSize };
        if (AuditLogFilter.FindProblem(filter.From, filter.To) is { } problem) return BadRequest(problem);
        try
        {
            var csv = tab switch
            {
                AuditLogTab.Requests => AuditCsv.Requests(await AllPagesAsync(page => auditLog.GetRequestsAsync(viewer, filter with { Page = page }, cancellationToken))),
                AuditLogTab.Changes => AuditCsv.Changes(await AllPagesAsync(page => auditLog.GetDataChangesAsync(viewer, filter with { Page = page }, cancellationToken))),
                _ => AuditCsv.SecurityEvents(await AllPagesAsync(page => auditLog.GetSecurityEventsAsync(viewer, filter with { Page = page }, cancellationToken))),
            };
            var name = $"audit-{tab.ToString().ToLowerInvariant()}-{filter.From:yyyyMMdd}-{filter.To:yyyyMMdd}.csv";
            // The byte order mark lets spreadsheet programs read the file as UTF-8.
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", name);
        }
        catch (AuditLogAccessDeniedException) { return Forbid(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
    }

    private async Task<IActionResult> ShowAsync(AuditLogTab tab, AuditLogQuery query, CancellationToken cancellationToken)
    {
        if (Viewer() is not { } viewer) return Forbid();
        var filter = Filter(query);
        string? problem = !ModelState.IsValid ? "Check the filter: one of the values is not valid." : AuditLogFilter.FindProblem(filter.From, filter.To);
        if (problem is not null)
        {
            // A filter the user can correct: show the form again with the reason, as a 400.
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("Index", Model(tab, viewer, filter, error: problem));
        }

        try
        {
            return View("Index", tab switch
            {
                AuditLogTab.Requests => Model(tab, viewer, filter, requests: await auditLog.GetRequestsAsync(viewer, filter, cancellationToken)),
                AuditLogTab.Changes => Model(tab, viewer, filter, changes: await auditLog.GetDataChangesAsync(viewer, filter, cancellationToken)),
                _ => Model(tab, viewer, filter, securityEvents: await auditLog.GetSecurityEventsAsync(viewer, filter, cancellationToken)),
            });
        }
        catch (AuditLogAccessDeniedException) { return Forbid(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
    }

    private AuditLogViewModel Model(AuditLogTab tab, AuditViewer viewer, AuditLogFilter filter, AuditLogPage<RequestAuditItem>? requests = null,
        AuditLogPage<DataChangeItem>? changes = null, AuditLogPage<SecurityEventItem>? securityEvents = null, string? error = null)
    {
        // The list shows the branch it was scoped to, also for a manager who did not pick one.
        var shown = viewer.CanViewAllBranches ? filter : filter with { BranchCode = viewer.HomeBranchCode };
        var (kinds, results) = tab switch
        {
            AuditLogTab.Requests => (ControllerNames(), Options(["Succeeded", "Failed"])),
            AuditLogTab.Changes => (Options(auditLog.AuditedTables), Options(Enum.GetNames<DataChangeOperation>())),
            _ => (Enum.GetValues<SecurityEventType>().Select(value => new SelectListItem(DisplayText.Of(value), value.ToString())).ToArray(), Array.Empty<SelectListItem>()),
        };
        return new AuditLogViewModel(tab, shown.Normalized(), Today(), viewer.CanViewAllBranches, viewer.CanViewSecurityEvents, kinds, results, requests, changes, securityEvents, error);
    }

    private AuditLogFilter Filter(AuditLogQuery query)
    {
        var today = Today();
        var to = query.To ?? (query.From is { } start && start > today ? start : today);
        var from = query.From ?? to.AddDays(-6);
        return new AuditLogFilter(from, to, query.BranchCode, query.StaffId, query.Kind, query.Result, query.CorrelationId, query.Page);
    }

    private AuditViewer? Viewer() =>
        Enum.TryParse<StaffRole>(User.FindFirstValue(ClaimTypes.Role), out var role)
            ? new AuditViewer(role, User.FindFirstValue(ApexPostClaimTypes.BranchCode))
            : null;

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private SelectListItem[] ControllerNames() =>
        Options(actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Select(action => action.ControllerName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static SelectListItem[] Options(IEnumerable<string> values) => [.. values.Select(value => new SelectListItem(value, value))];

    private static async Task<List<T>> AllPagesAsync<T>(Func<int, Task<AuditLogPage<T>>> read)
    {
        var rows = new List<T>();
        for (var page = 1; rows.Count < ExportLimit; page++)
        {
            var result = await read(page);
            rows.AddRange(result.Items);
            if (page >= result.TotalPages) break;
        }

        return rows.Count > ExportLimit ? rows[..ExportLimit] : rows;
    }
}
