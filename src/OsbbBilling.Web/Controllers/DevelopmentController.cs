using Microsoft.AspNetCore.Mvc;
using OsbbBilling.Web.Services;

namespace OsbbBilling.Web.Controllers;

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
