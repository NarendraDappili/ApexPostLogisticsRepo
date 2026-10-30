using ApexPost.Application.MoneyOrders;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

[Authorize(Roles = "CounterClerk")]
public sealed class MoneyOrdersController(MoneyOrderWorkflowService moneyOrders) : Controller
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Verify(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var order = await moneyOrders.GetAsync(new MoneyOrderId(id), cancellationToken);
            if (!IsOwnBranch(order)) return Forbid();
            return View(new MoneyOrderVerificationViewModel { MoneyOrderId = order.Id.Value, Summary = Summarize(order) });
        }
        catch (MoneyOrderNotFoundException) { return NotFound(); }
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Never echo the raw document number back to the browser.
            input.DocumentNumber = string.Empty;
            ModelState.Remove(nameof(input.DocumentNumber));
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }

    private bool IsOwnBranch(MoneyOrder order) =>
        string.Equals(User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value, order.BranchCode.Value, StringComparison.OrdinalIgnoreCase);

    private MoneyOrderSummaryViewModel Summarize(MoneyOrder order) => new(
        order.TransactionContextId.Value, order.Amount.Amount, order.Fee.Amount, order.Total.Amount,
        moneyOrders.RequiresVerification(order), order.IdentityVerificationOutcome, order.IdentityVerification?.MaskedDocumentReference.Value);
}
