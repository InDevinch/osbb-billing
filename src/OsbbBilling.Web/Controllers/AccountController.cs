using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Web.Controllers;

public class AccountController(
    OsbbDbContext database,
    IPasswordHasher<AppUser> passwordHasher) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return AfterLogin(returnUrl);

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var login = model.Login.Trim();
            var user = await database.Users.SingleOrDefaultAsync(x => x.Login == login, cancellationToken);
            var verification = PasswordVerificationResult.Failed;
            if (user is { IsActive: true })
            {
                try
                {
                    verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
                }
                catch (FormatException)
                {
                    verification = PasswordVerificationResult.Failed;
                }
            }

            if (user is null || verification == PasswordVerificationResult.Failed
                || user.RoleCode is not (Roles.Accountant or Roles.Chairperson or Roles.Owner)
                || (user.RoleCode == Roles.Owner && user.OwnerId is null))
            {
                ModelState.AddModelError("", "Неправильний логін або пароль.");
                return View(model);
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, model.Password);
                await database.SaveChangesAsync(cancellationToken);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Login),
                new(ClaimTypes.Role, user.RoleCode)
            };
            if (user.OwnerId.HasValue)
                claims.Add(new Claim("OwnerId", user.OwnerId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
            return AfterLogin(model.ReturnUrl);
        }
        catch (SqlException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", "Вхід тимчасово недоступний. Спробуйте пізніше.");
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private IActionResult AfterLogin(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl!)
            : RedirectToAction("Index", "Workspace");
}
