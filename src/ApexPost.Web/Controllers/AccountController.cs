using ApexPost.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Web.Controllers;

public sealed class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Login(LoginViewModel input)
    {
        if (!ModelState.IsValid) return View(input);
        ModelState.AddModelError(string.Empty, "Authentication is not implemented yet. This screen is a UI placeholder only.");
        return View(input);
    }
}
