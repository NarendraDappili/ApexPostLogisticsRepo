using ApexPost.Application.Transactions;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Filters;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class CounterTransactionsController(CounterTransactionWorkflowService transactions) : Controller
{
    [Authorize(Roles = "CounterClerk"), HttpGet]
    public IActionResult New() => View(new CounterBasketViewModel { ClerkId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value ?? string.Empty, BranchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value ?? string.Empty });

    [Authorize(Roles = "CounterClerk"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CounterBasketViewModel input, CancellationToken cancellationToken)
    {
        if (input.RetailQuantity.HasValue != !string.IsNullOrWhiteSpace(input.ProductId)) ModelState.AddModelError(nameof(input.ProductId), "A retail product and quantity must be supplied together.");
        if (input.ServiceUnitPrice.HasValue != !string.IsNullOrWhiteSpace(input.ServiceDescription)) ModelState.AddModelError(nameof(input.ServiceDescription), "A service description and service price must be supplied together.");
        if (!input.RetailQuantity.HasValue && !input.ServiceUnitPrice.HasValue && string.IsNullOrWhiteSpace(input.MoneyOrderIds)) ModelState.AddModelError(string.Empty, "Add at least one retail item, service, or money order.");
        if (!ModelState.IsValid) return View(input);
        var staffId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        var branchCode = User.FindFirst(ApexPostClaimTypes.BranchCode)?.Value;
        if (string.IsNullOrWhiteSpace(staffId) || !string.Equals(branchCode, input.BranchCode, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try
        {
            var retail = input.RetailQuantity.HasValue ? new[] { new RetailBasketLineRequest(input.ProductId!, input.RetailQuantity.Value) } : [];
            var services = input.ServiceUnitPrice.HasValue ? new[] { new ServiceBasketLineRequest(input.ServiceDescription!, input.ServiceUnitPrice.Value, input.ServiceQuantity) } : [];
            var moneyOrders = string.IsNullOrWhiteSpace(input.MoneyOrderIds) ? [] : input.MoneyOrderIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var transaction = await transactions.CheckoutAsync(new CounterCheckoutRequest(input.TransactionId, input.BranchCode, input.CounterCode, staffId, input.PaymentMethod, retail, services, moneyOrders), cancellationToken);
            TempData["Notice"] = $"Transaction {transaction.Id.Value} was paid.";
            return RedirectToAction(nameof(New));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { ModelState.AddModelError(string.Empty, exception.Message); return View(input); }
    }

    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), RequireIpAllowlist, HttpPost, ValidateAntiForgeryToken, InvalidModelResponse]
    public Task<IActionResult> Void(TransactionReversalViewModel input, CancellationToken cancellationToken) => ReverseAsync(input, refund: false, cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.BranchManagerOnly), RequireIpAllowlist, HttpPost, ValidateAntiForgeryToken, InvalidModelResponse]
    public Task<IActionResult> Refund(TransactionReversalViewModel input, CancellationToken cancellationToken) => ReverseAsync(input, refund: true, cancellationToken);

    private async Task<IActionResult> ReverseAsync(TransactionReversalViewModel input, bool refund, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var managerId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        if (string.IsNullOrWhiteSpace(managerId)) return Forbid();
        try
        {
            if (refund) await transactions.RefundAsync(new CounterTransactionId(input.TransactionId), new ManagerTransactionActionRequest(managerId, input.Reason), cancellationToken).ConfigureAwait(false);
            else await transactions.VoidAsync(new CounterTransactionId(input.TransactionId), new ManagerTransactionActionRequest(managerId, input.Reason), cancellationToken).ConfigureAwait(false);
            TempData["Notice"] = refund ? "Transaction refunded." : "Transaction voided.";
            return RedirectToAction(nameof(New));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return BadRequest(exception.Message); }
    }
}
