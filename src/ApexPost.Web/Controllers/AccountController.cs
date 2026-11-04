using ApexPost.Application.Auditing;
using ApexPost.Web.Models;
using ApexPost.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexPost.Web.Controllers;

public sealed class AccountController(LocalStaffAuthenticator authenticator) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel input, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(input);
        var outcome = await authenticator.AuthenticateWithOutcomeAsync(input.StaffId, input.Password, cancellationToken).ConfigureAwait(false);
        if (outcome.Principal is not { } principal)
        {
            await SecurityEventRecorder.RecordAsync(HttpContext, SecurityEventType.SignInFailed, StatusCodes.Status200OK, outcome.FailureReason, attemptedStaffId: outcome.StaffId).ConfigureAwait(false);
            input.Password = string.Empty;
            ModelState.Remove(nameof(input.Password));
            ModelState.AddModelError(string.Empty, "Sign-in failed.");
            return View(input);
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true }).ConfigureAwait(false);
        await SecurityEventRecorder.RecordAsync(HttpContext, SecurityEventType.SignInSucceeded, StatusCodes.Status302Found, principal: principal).ConfigureAwait(false);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Dashboard", "Branch")!);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Recorded first, while the request still carries the staff member's claims.
        await SecurityEventRecorder.RecordAsync(HttpContext, SecurityEventType.SignedOut, StatusCodes.Status302Found).ConfigureAwait(false);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();
}
