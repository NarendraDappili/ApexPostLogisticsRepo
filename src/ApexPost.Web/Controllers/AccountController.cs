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
        var principal = await authenticator.AuthenticateAsync(input.StaffId, input.Password, cancellationToken).ConfigureAwait(false);
        if (principal is null)
        {
            input.Password = string.Empty;
            ModelState.Remove(nameof(input.Password));
            ModelState.AddModelError(string.Empty, "Sign-in failed.");
            return View(input);
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true }).ConfigureAwait(false);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Dashboard", "Branch")!);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();
}
