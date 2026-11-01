using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Queries;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Middleware;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk")]
public sealed class MoneyOrdersController(MoneyOrderWorkflowService moneyOrders, IMoneyOrderQueries moneyOrderQueries) : Controller
{
    [HttpGet]
    public IActionResult Create() => View(new MoneyOrderFormViewModel
    {
        BranchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value ?? string.Empty
    });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MoneyOrderFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        if (!string.Equals(User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value, input.BranchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try
        {
            var result = await moneyOrders.CreateAsync(new MoneyOrderCreationRequest(input.MoneyOrderId, input.BranchCode, input.Amount, input.Sender, input.Recipient, input.TransactionContextId), cancellationToken);
            var total = result.Total.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            if (result.RequiresIdentityVerification)
            {
                TempData["Notice"] = $"Money order {result.MoneyOrder.Id.Value} was issued. Total: {total}. Verify the sender's identity before adding it to the basket.";
                return RedirectToAction(nameof(Verify), new { id = result.MoneyOrder.Id.Value });
            }

            TempData["Notice"] = $"Money order {result.MoneyOrder.Id.Value} was created. Total: {total}.";
            return RedirectToAction(nameof(Create));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (ExceptionProblemMapping.Map(exception) is { } problem)
        {
            this.UseConflictStatus(problem);
            ModelState.AddModelError(string.Empty, problem.Detail);
            return View(input);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Verify(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            // Read side: a no-tracking projection with the verification joined, not the tracked aggregate.
            var order = await moneyOrderQueries.GetSummaryAsync(new MoneyOrderId(id), cancellationToken);
            if (order is null) return NotFound();
            if (!IsOwnBranch(order)) return Forbid();
            return View(new MoneyOrderVerificationViewModel { MoneyOrderId = order.MoneyOrderId, Summary = Summarize(order) });
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(MoneyOrderVerificationViewModel input, CancellationToken cancellationToken)
    {
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(branchCode)) return Forbid();
        try
        {
            var order = await moneyOrders.VerifyIdentityAsync(new MoneyOrderId(input.MoneyOrderId), new BranchCode(branchCode), new IdentityVerificationRequest(input.DocumentType, input.DocumentNumber), cancellationToken);
            TempData["Notice"] = $"Identity verification for {order.Id.Value}: {order.IdentityVerificationOutcome}.";
            return RedirectToAction(nameof(Verify), new { id = order.Id.Value });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (MoneyOrderNotFoundException) { return NotFound(); }
        catch (Exception exception) when (ExceptionProblemMapping.Map(exception) is { } problem)
        {
            this.UseConflictStatus(problem);
            // Never echo the raw document number back to the browser.
            input.DocumentNumber = string.Empty;
            ModelState.Remove(nameof(input.DocumentNumber));
            ModelState.AddModelError(string.Empty, problem.Detail);
            input.Summary = await OwnSummaryAsync(input.MoneyOrderId);
            return View(input);
        }
    }

    // The order details for a redisplayed form; none when the ID is malformed, unknown or another branch's.
    private async Task<MoneyOrderSummaryViewModel?> OwnSummaryAsync(string moneyOrderId)
    {
        MoneyOrderId id;
        try { id = new MoneyOrderId(moneyOrderId); }
        catch (ArgumentException) { return null; }

        var order = await moneyOrderQueries.GetSummaryAsync(id, CancellationToken.None);
        return order is not null && IsOwnBranch(order) ? Summarize(order) : null;
    }

    private bool IsOwnBranch(MoneyOrderSummary order) =>
        string.Equals(User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value, order.BranchCode, StringComparison.OrdinalIgnoreCase);

    private MoneyOrderSummaryViewModel Summarize(MoneyOrderSummary order) => new(
        order.TransactionId, order.Amount, order.Fee, order.Total,
        moneyOrders.RequiresVerification(new Money(order.Amount)), order.Outcome, order.MaskedDocumentReference);
}
