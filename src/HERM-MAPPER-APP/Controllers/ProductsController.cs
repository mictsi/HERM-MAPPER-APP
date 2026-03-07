using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Infrastructure;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ProductsController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    ConfigurableFieldService configurableFieldService) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .AsQueryable();

        var likePattern = SearchPattern.CreateContainsPattern(search);
        if (likePattern is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Name, likePattern) ||
                (x.Vendor != null && EF.Functions.Like(x.Vendor, likePattern)) ||
                (x.Version != null && EF.Functions.Like(x.Version, likePattern)) ||
                (x.Owner != null && EF.Functions.Like(x.Owner, likePattern)) ||
                (x.LifecycleStatus != null && EF.Functions.Like(x.LifecycleStatus, likePattern)) ||
                (x.Description != null && EF.Functions.Like(x.Description, likePattern)) ||
                (x.Notes != null && EF.Functions.Like(x.Notes, likePattern)));
        }

        var model = new ProductsIndexViewModel
        {
            Search = search,
            Products = await query.OrderBy(x => x.Name).ToListAsync()
        };

        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateFormOptionsAsync(null, null);
        return View(new ProductCatalogItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Vendor,Version,LifecycleStatus,Owner,Description,Notes")] ProductCatalogItem input)
    {
        input.Owner = NormalizeSelection(input.Owner);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);

        if (!ModelState.IsValid)
        {
            await PopulateFormOptionsAsync(input.Owner, input.LifecycleStatus);
            return View(input);
        }

        input.CreatedUtc = DateTime.UtcNow;
        input.UpdatedUtc = DateTime.UtcNow;

        dbContext.ProductCatalogItems.Add(input);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Product",
            "Create",
            nameof(ProductCatalogItem),
            input.Id,
            $"Created product {input.Name}.");

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

    public async Task<IActionResult> Visualize(int id)
    {
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmCapability)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .ThenInclude(x => x!.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (product is null)
        {
            return NotFound();
        }

        var paths = product.Mappings
            .SelectMany(mapping =>
            {
                var capabilityLinks = mapping.TrmComponent?.CapabilityLinks
                    .Where(x => x.TrmCapability is not null)
                    .ToList();

                if (capabilityLinks is null || capabilityLinks.Count == 0)
                {
                    return
                    [
                        new ProductDependencyPathViewModel
                        {
                            Status = mapping.MappingStatus.ToString(),
                            DomainLabel = mapping.TrmDomain is null ? "-" : $"{mapping.TrmDomain.Code} {mapping.TrmDomain.Name}",
                            CapabilityLabel = mapping.TrmCapability is null ? "-" : $"{mapping.TrmCapability.Code} {mapping.TrmCapability.Name}",
                            ComponentLabel = mapping.TrmComponent is null ? "-" : mapping.TrmComponent.DisplayLabel
                        }
                    ];
                }

                return capabilityLinks
                    .Select(link => new ProductDependencyPathViewModel
                    {
                        Status = mapping.MappingStatus.ToString(),
                        DomainLabel = link.TrmCapability?.ParentDomain is null ? "-" : $"{link.TrmCapability.ParentDomain.Code} {link.TrmCapability.ParentDomain.Name}",
                        CapabilityLabel = link.TrmCapability is null ? "-" : $"{link.TrmCapability.Code} {link.TrmCapability.Name}",
                        ComponentLabel = mapping.TrmComponent?.DisplayLabel ?? "-"
                    });
            })
            .DistinctBy(x => new { x.DomainLabel, x.CapabilityLabel, x.ComponentLabel, x.Status })
            .OrderBy(x => x.DomainLabel)
            .ThenBy(x => x.CapabilityLabel)
            .ThenBy(x => x.ComponentLabel)
            .ToList();

        return View(new ProductVisualizationViewModel
        {
            Product = product,
            Paths = paths
        });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await dbContext.ProductCatalogItems.FindAsync(id);
        if (product is not null)
        {
            await PopulateFormOptionsAsync(product.Owner, product.LifecycleStatus);
        }

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

        input.Owner = NormalizeSelection(input.Owner);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);

        if (!ModelState.IsValid)
        {
            input.Id = id;
            await PopulateFormOptionsAsync(input.Owner, input.LifecycleStatus);
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
        await auditLogService.WriteAsync(
            "Product",
            "Update",
            nameof(ProductCatalogItem),
            product.Id,
            $"Updated product {product.Name}.");
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
        await auditLogService.WriteAsync(
            "Product",
            "Delete",
            nameof(ProductCatalogItem),
            id,
            $"Deleted product {product.Name}.");
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateFormOptionsAsync(string? selectedOwner, string? selectedLifecycleStatus)
    {
        ViewData["OwnerOptions"] = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.Owner,
            selectedOwner);
        ViewData["LifecycleStatusOptions"] = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            selectedLifecycleStatus);
    }

    private static string? NormalizeSelection(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
