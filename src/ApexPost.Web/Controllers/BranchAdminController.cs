using ApexPost.Application.BranchManagement;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

/// <summary>Branch administration. Administrator-only and, as an admin endpoint, limited to the configured IP allowlist.</summary>
[Authorize(Roles = "Administrator"), RequireIpAllowlist]
public sealed class BranchAdminController(BranchManagementService branchManagement) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await branchManagement.GetAllAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View(new BranchFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BranchFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        try
        {
            var branch = await branchManagement.CreateAsync(ToRequest(input), input.CounterCount, cancellationToken);
            TempData["Notice"] = $"Branch {branch.Code.Value} was added with {input.CounterCount} counter terminal(s).";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var branch = await branchManagement.GetAsync(new BranchCode(id), cancellationToken);
            return View(new BranchFormViewModel { BranchCode = branch.Code.Value, Name = branch.Name, Region = branch.Region, OpeningHours = branch.OpeningHours, IsOpen = branch.IsOpen });
        }
        catch (BranchNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BranchFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        try
        {
            var branch = await branchManagement.UpdateAsync(ToRequest(input), cancellationToken);
            TempData["Notice"] = $"Branch {branch.Code.Value} was updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (BranchNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(input);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Close(string id, CancellationToken cancellationToken) => SetOpenAsync(id, open: false, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Open(string id, CancellationToken cancellationToken) => SetOpenAsync(id, open: true, cancellationToken);

    private async Task<IActionResult> SetOpenAsync(string id, bool open, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var branch = await branchManagement.SetOpenAsync(new BranchCode(id), open, cancellationToken);
            TempData["Notice"] = $"Branch {branch.Code.Value} is now {(open ? "open" : "closed")}.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (BranchNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
    }

    private static BranchDetailsRequest ToRequest(BranchFormViewModel input) => new(input.BranchCode, input.Name, input.Region, input.OpeningHours);
}
