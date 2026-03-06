using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ProductsController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Name.Contains(search) ||
                (x.Vendor != null && x.Vendor.Contains(search)) ||
                (x.Version != null && x.Version.Contains(search)) ||
                (x.Owner != null && x.Owner.Contains(search)));
        }

        var model = new ProductsIndexViewModel
        {
            Search = search,
            Products = await query.OrderBy(x => x.Name).ToListAsync()
        };

        return View(model);
    }

    public IActionResult Create()
    {
        return View(new ProductCatalogItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Vendor,Version,LifecycleStatus,Owner,Description,Notes")] ProductCatalogItem input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        input.CreatedUtc = DateTime.UtcNow;
        input.UpdatedUtc = DateTime.UtcNow;

        dbContext.ProductCatalogItems.Add(input);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmCapability)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .FirstOrDefaultAsync(x => x.Id == id);

        return product is null ? NotFound() : View(product);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await dbContext.ProductCatalogItems.FindAsync(id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Name,Vendor,Version,LifecycleStatus,Owner,Description,Notes")] ProductCatalogItem input)
    {
        var product = await dbContext.ProductCatalogItems.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        product.Name = input.Name;
        product.Vendor = input.Vendor;
        product.Version = input.Version;
        product.LifecycleStatus = input.LifecycleStatus;
        product.Owner = input.Owner;
        product.Description = input.Description;
        product.Notes = input.Notes;
        product.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .FirstOrDefaultAsync(x => x.Id == id);

        return product is null ? NotFound() : View(product);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await dbContext.ProductCatalogItems.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        dbContext.ProductCatalogItems.Remove(product);
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
