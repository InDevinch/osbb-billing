using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;

namespace OsbbBilling.Web.Security;

public class UserCookieEvents(OsbbDbContext database) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal!;
        var validId = int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var user = validId
            ? await database.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.UserId == userId, context.HttpContext.RequestAborted)
            : null;

        // Зміна ролі, платника або блокування користувача скасовує попередній вхід.
        if (user is null || !user.IsActive
            || user.Login != principal.Identity?.Name
            || user.RoleCode != principal.FindFirstValue(ClaimTypes.Role)
            || user.OwnerId?.ToString() != principal.FindFirstValue("OwnerId"))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
