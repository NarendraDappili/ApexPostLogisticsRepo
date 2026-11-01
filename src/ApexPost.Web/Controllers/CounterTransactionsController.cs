using ApexPost.Application.Queries;
using ApexPost.Application.Transactions;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Filters;
using ApexPost.Web.Middleware;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class CounterTransactionsController(CounterTransactionWorkflowService transactions, ITransactionQueries transactionHistory) : Controller
{
    [Authorize(Roles = "CounterClerk"), HttpGet]
    public IActionResult New() => View(new CounterBasketViewModel
    {
        BranchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value ?? string.Empty
    }.WithBlankRetailRows());

    [Authorize(Roles = "CounterClerk"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CounterBasketViewModel input, CancellationToken cancellationToken)
    {
        var retailLines = input.RetailLines.Where(line => !string.IsNullOrWhiteSpace(line.ProductId) || line.Quantity.HasValue).ToArray();
        if (retailLines.Any(line => string.IsNullOrWhiteSpace(line.ProductId) || !line.Quantity.HasValue)) ModelState.AddModelError(nameof(input.RetailLines), "Each retail row needs both a product ID and a quantity.");
        var shipmentIds = SplitIds(input.ShipmentIds);
        var moneyOrderIds = SplitIds(input.MoneyOrderIds);
        if (retailLines.Length == 0 && shipmentIds.Length == 0 && moneyOrderIds.Length == 0) ModelState.AddModelError(string.Empty, "Add at least one retail item, shipment, or money order.");
        if (!ModelState.IsValid) return View(input.WithBlankRetailRows());
        var staffId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(staffId) || !string.Equals(branchCode, input.BranchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try
        {
            var retail = retailLines.Select(line => new RetailBasketLineRequest(line.ProductId!, line.Quantity!.Value)).ToArray();
            var transaction = await transactions.CheckoutAsync(new CounterCheckoutRequest(input.TransactionId, input.BranchCode, input.CounterCode, staffId, input.PaymentMethod, retail, shipmentIds, moneyOrderIds), cancellationToken);
            TempData["Notice"] = $"Transaction {transaction.Id.Value} was paid. Total: {transaction.Total.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.";
            return RedirectToAction(nameof(New));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (ExceptionProblemMapping.Map(exception) is { } problem) { this.UseConflictStatus(problem); ModelState.AddModelError(string.Empty, problem.Detail); return View(input.WithBlankRetailRows()); }
    }

    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), HttpGet]
    public async Task<IActionResult> Manage(CancellationToken cancellationToken)
    {
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(branchCode)) return Forbid();
        return View(await ManageModelAsync(branchCode, cancellationToken));
    }

    // Void and refund are counter operations at the manager's own branch. The workflow enforces an active Branch
    // Manager of the transaction's branch, so they are not restricted to the admin/integration IP allowlist.
    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), HttpPost, ValidateAntiForgeryToken, HandlesInvalidModel]
    public Task<IActionResult> Void(TransactionReversalViewModel input, CancellationToken cancellationToken) => ReverseAsync(input, refund: false, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), HttpPost, ValidateAntiForgeryToken, HandlesInvalidModel]
    public Task<IActionResult> Refund(TransactionReversalViewModel input, CancellationToken cancellationToken) => ReverseAsync(input, refund: true, cancellationToken);

    // A refused void or refund redisplays the Manage page with the reason, keeping the status code (400, 404 or 409)
    // so the audit trail and API clients still see the failure.
    private async Task<IActionResult> ReverseAsync(TransactionReversalViewModel input, bool refund, CancellationToken cancellationToken)
    {
        var managerId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(managerId) || string.IsNullOrWhiteSpace(branchCode)) return Forbid();
        var action = refund ? "refunded" : "voided";
        if (!ModelState.IsValid)
        {
            return await ManageWithErrorAsync(branchCode, StatusCodes.Status400BadRequest, $"The transaction was not {action}: enter the transaction and a reason of at most 500 characters.", cancellationToken);
        }

        try
        {
            if (refund) await transactions.RefundAsync(new CounterTransactionId(input.TransactionId), new ManagerTransactionActionRequest(managerId, input.Reason), cancellationToken).ConfigureAwait(false);
            else await transactions.VoidAsync(new CounterTransactionId(input.TransactionId), new ManagerTransactionActionRequest(managerId, input.Reason), cancellationToken).ConfigureAwait(false);
            TempData["Notice"] = $"Transaction {input.TransactionId} {action}.";
            return RedirectToAction(nameof(Manage));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (ExceptionProblemMapping.Map(exception) is { } problem)
        {
            return await ManageWithErrorAsync(branchCode, problem.Status, $"Transaction {input.TransactionId} was not {action}: {problem.Detail}", cancellationToken);
        }
    }

    private async Task<IActionResult> ManageWithErrorAsync(string branchCode, int status, string error, CancellationToken cancellationToken)
    {
        var model = await ManageModelAsync(branchCode, cancellationToken);
        Response.StatusCode = status;
        ViewData["Error"] = error;
        return View(nameof(Manage), model);
    }

    private async Task<TransactionListViewModel> ManageModelAsync(string branchCode, CancellationToken cancellationToken)
    {
        var history = await transactionHistory.GetRecentAsync(new BranchCode(branchCode), 50, cancellationToken);
        return new TransactionListViewModel(branchCode, history
            .Select(transaction => new TransactionSummaryViewModel(transaction.TransactionId, transaction.OccurredAtUtc, transaction.CounterCode, transaction.ClerkId, transaction.Total, transaction.Status, transaction.StatusReason))
            .ToArray());
    }

    private static string[] SplitIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids) ? [] : ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
