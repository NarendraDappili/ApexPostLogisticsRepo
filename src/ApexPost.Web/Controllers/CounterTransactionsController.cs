using ApexPost.Application.Transactions;
using ApexPost.Domain.Enums;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class CounterTransactionsController(CounterTransactionWorkflowService transactions) : Controller
{
    [HttpGet]
    public IActionResult New() => View(new CounterBasketViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CounterBasketViewModel input, CancellationToken cancellationToken)
    {
        if (input.RetailQuantity.HasValue != !string.IsNullOrWhiteSpace(input.ProductId)) ModelState.AddModelError(nameof(input.ProductId), "A retail product and quantity must be supplied together.");
        if (input.ServiceUnitPrice.HasValue != !string.IsNullOrWhiteSpace(input.ServiceDescription)) ModelState.AddModelError(nameof(input.ServiceDescription), "A service description and service price must be supplied together.");
        if (!input.RetailQuantity.HasValue && !input.ServiceUnitPrice.HasValue && string.IsNullOrWhiteSpace(input.MoneyOrderIds)) ModelState.AddModelError(string.Empty, "Add at least one retail item, service, or money order.");
        if (!ModelState.IsValid) return View(input);
        try
        {
            var retail = input.RetailQuantity.HasValue ? new[] { new RetailBasketLineRequest(input.ProductId!, input.RetailQuantity.Value) } : [];
            var services = input.ServiceUnitPrice.HasValue ? new[] { new ServiceBasketLineRequest(input.ServiceDescription!, input.ServiceUnitPrice.Value, input.ServiceQuantity) } : [];
            var moneyOrders = string.IsNullOrWhiteSpace(input.MoneyOrderIds) ? [] : input.MoneyOrderIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var transaction = await transactions.CheckoutAsync(new CounterCheckoutRequest(input.TransactionId, input.BranchCode, input.CounterCode, input.ClerkId, input.PaymentMethod, retail, services, moneyOrders), cancellationToken);
            TempData["Notice"] = $"Transaction {transaction.Id.Value} was paid.";
            return RedirectToAction(nameof(New));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { ModelState.AddModelError(string.Empty, exception.Message); return View(input); }
    }
}
