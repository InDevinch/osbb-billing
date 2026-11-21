using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;
using OsbbBilling.Web.Models;
using OsbbBilling.Web.Models.Entities;
using OsbbBilling.Web.Security;

namespace OsbbBilling.Web.Controllers;

[Authorize(Roles = Roles.Accountant + "," + Roles.Chairperson)]
public class OwnersController(OsbbDbContext database) : Controller
{
    private const int PageSize = 20;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        var query = database.Owners.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(x => x.FullName.Contains(search)
                || (x.Phone != null && x.Phone.Contains(search))
                || (x.Email != null && x.Email.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);
        var owners = await query.OrderBy(x => x.FullName).ThenBy(x => x.OwnerId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(x => new OwnerListItemViewModel
            {
                OwnerId = x.OwnerId,
                FullName = x.FullName,
                Phone = x.Phone,
                Email = x.Email,
                AccountCount = x.Accounts.Count
            })
            .ToListAsync(cancellationToken);

        return View(new OwnerListViewModel
        {
            Owners = owners,
            Search = search,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, CancellationToken cancellationToken)
    {
        var owner = await database.Owners.AsNoTracking()
            .Include(x => x.Accounts).ThenInclude(x => x.Premise).ThenInclude(x => x.Building)
            .SingleOrDefaultAsync(x => x.OwnerId == id, cancellationToken);
        return owner is null ? NotFound() : View(owner);
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public IActionResult Create() => View(new OwnerFormViewModel());

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Create(OwnerFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var owner = new Owner
        {
            FullName = model.FullName.Trim(),
            Phone = OptionalText(model.Phone),
            Email = OptionalText(model.Email)
        };
        database.Owners.Add(owner);
        await database.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Власника додано.";
        return RedirectToAction(nameof(Details), new { id = owner.OwnerId });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, CancellationToken cancellationToken)
    {
        var owner = await database.Owners.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == id, cancellationToken);
        if (owner is null)
            return NotFound();

        return View(new OwnerFormViewModel
        {
            FullName = owner.FullName,
            Phone = owner.Phone,
            Email = owner.Email
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.Accountant)]
    public async Task<IActionResult> Edit([FromRoute] int id, OwnerFormViewModel model,
        CancellationToken cancellationToken)
    {
        var owner = await database.Owners.SingleOrDefaultAsync(x => x.OwnerId == id, cancellationToken);
        if (owner is null)
            return NotFound();
        if (!ModelState.IsValid)
            return View(model);

        owner.FullName = model.FullName.Trim();
        owner.Phone = OptionalText(model.Phone);
        owner.Email = OptionalText(model.Email);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotFound();
        }
        TempData["Success"] = "Зміни збережено.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
