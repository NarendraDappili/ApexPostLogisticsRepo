using ApexPost.Application.StaffManagement;
using ApexPost.Domain.ValueObjects;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

/// <summary>Staff administration. Administrator-only and, as an admin endpoint, limited to the configured IP allowlist.</summary>
[Authorize(Roles = "Administrator"), RequireIpAllowlist]
public sealed class StaffAdminController(StaffManagementService staffManagement) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await staffManagement.GetAllAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View(new StaffFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffFormViewModel input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input.Password)) ModelState.AddModelError(nameof(input.Password), "An initial password is required.");
        if (!ModelState.IsValid) return View(WithoutPassword(input));
        try
        {
            var member = await staffManagement.CreateAsync(ToRequest(input), input.Password!, cancellationToken);
            TempData["Notice"] = $"Staff member {member.Id.Value} was added.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            this.UseConflictStatus(exception);
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(WithoutPassword(input));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var member = await staffManagement.GetAsync(new StaffId(id), cancellationToken);
            return View(new StaffFormViewModel { StaffId = member.Id.Value, Name = member.Name, Email = member.Email ?? string.Empty, Role = member.Role, BranchCode = member.AssignedBranchCode.Value, IsActive = member.IsActive });
        }
        catch (StaffMemberNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StaffFormViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(WithoutPassword(input));
        try
        {
            var member = await staffManagement.UpdateAsync(ToRequest(input), input.Password, cancellationToken);
            TempData["Notice"] = $"Staff member {member.Id.Value} was updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (StaffMemberNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            this.UseConflictStatus(exception);
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(WithoutPassword(input));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(string id, CancellationToken cancellationToken) => SetActiveAsync(id, active: false, cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(string id, CancellationToken cancellationToken) => SetActiveAsync(id, active: true, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(string id, bool active, CancellationToken cancellationToken)
    {
        var actingStaffId = User.FindFirst(ApexPostClaimTypes.StaffId)?.Value;
        if (string.IsNullOrWhiteSpace(actingStaffId)) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            var member = await staffManagement.SetActiveAsync(new StaffId(id), active, new StaffId(actingStaffId), cancellationToken);
            TempData["Notice"] = $"Staff member {member.Id.Value} was {(active ? "reactivated" : "deactivated")}.";
            return RedirectToAction(nameof(Index));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
    }

    private static StaffDetailsRequest ToRequest(StaffFormViewModel input) => new(input.StaffId, input.Name, input.Email, input.Role, input.BranchCode);

    private StaffFormViewModel WithoutPassword(StaffFormViewModel input)
    {
        // Never send a typed password back to the browser.
        input.Password = null;
        ModelState.Remove(nameof(input.Password));
        return input;
    }
}
