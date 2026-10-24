using ApexPost.Application.MoneyOrders;
using ApexPost.Domain.Enums;
using ApexPost.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class MoneyOrdersController(MoneyOrderWorkflowService moneyOrders) : Controller
{
    [HttpGet]
    public IActionResult Create() => View(new MoneyOrderFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MoneyOrderFormViewModel input, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.DocumentNumber) && string.IsNullOrWhiteSpace(input.DocumentType)) ModelState.AddModelError(nameof(input.DocumentType), "Document type is required when a document reference is provided.");
        if (!ModelState.IsValid) return View(input);
        try
        {
            IdentityVerificationRequest? verification = string.IsNullOrWhiteSpace(input.DocumentNumber)
                ? null
                : new IdentityVerificationRequest(input.TransactionContextId, input.DocumentType!, input.DocumentNumber, input.VerificationOutcome);
            var result = await moneyOrders.CreateAsync(new MoneyOrderCreationRequest(input.MoneyOrderId, input.BranchCode, input.Amount, input.Sender, input.Recipient, input.Fee, input.TransactionContextId, verification), cancellationToken);
            TempData["Notice"] = $"Money order {result.MoneyOrder.Id.Value} was created. Total: {result.Total.Amount:0.00}.";
            return RedirectToAction(nameof(Create));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IdentityVerificationRequiredException)
        {
            input.DocumentNumber = string.Empty;
            ModelState.Remove(nameof(input.DocumentNumber));
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }
}
