using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OsbbBilling.Web.Security;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant)]
public class DevelopmentController(
    IWebHostEnvironment environment,
    DatabaseConnectionCheck databaseCheck) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Database(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return NotFound();

        return View(await databaseCheck.CheckAsync(cancellationToken));
    }
}
