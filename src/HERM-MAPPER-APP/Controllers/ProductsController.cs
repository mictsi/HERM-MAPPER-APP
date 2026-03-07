using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ProductsController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    ConfigurableFieldService configurableFieldService,
    SampleRelationshipImportService sampleRelationshipImportService) : Controller
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
            ImportStatusMessage = TempData["ImportStatusMessage"] as string,
            Products = await query.OrderBy(x => x.Name).ToListAsync()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? csvFile)
    {
        if (csvFile is null || csvFile.Length == 0)
        {
            TempData["ImportStatusMessage"] = "Choose a CSV file before importing products.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ImportStatusMessage"] = "Only .csv files are supported for product import.";
            return RedirectToAction(nameof(Index));
        }

        ProductRelationshipImportSummary summary;
        await using (var stream = csvFile.OpenReadStream())
        {
            summary = await sampleRelationshipImportService.ImportAsync(stream);
        }

        await auditLogService.WriteAsync(
            "Product",
            "ImportCsv",
            nameof(ProductCatalogItem),
            null,
            $"Imported products from {csvFile.FileName}.",
            $"Rows read: {summary.RowsRead}; products added: {summary.ProductsAdded}; existing products matched: {summary.ProductsMatched}; mappings added: {summary.MappingsAdded}; product-only rows: {summary.ProductsOnlyRows}; duplicates skipped: {summary.MappingsSkippedAsDuplicate}; rows skipped: {summary.RowsSkipped}.");

        TempData["ImportStatusMessage"] =
            $"Imported {summary.ProductsAdded} new product(s), matched {summary.ProductsMatched} existing product(s), " +
            $"created {summary.MappingsAdded} mapping(s), and left {summary.ProductsOnlyRows} row(s) as product-only because the hierarchy did not match.";

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOwnerOptionsAsync(null);
        return View(new ProductCatalogItem());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Vendor,Version,LifecycleStatus,Owner,Description,Notes")] ProductCatalogItem input)
    {
        input.Owner = NormalizeSelection(input.Owner);

        if (!ModelState.IsValid)
        {
            await PopulateOwnerOptionsAsync(input.Owner);
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
            await PopulateOwnerOptionsAsync(product.Owner);
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

        if (!ModelState.IsValid)
        {
            input.Id = id;
            await PopulateOwnerOptionsAsync(input.Owner);
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

    private async Task PopulateOwnerOptionsAsync(string? selectedValue)
    {
        ViewData["OwnerOptions"] = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.Owner,
            selectedValue);
    }

    private static string? NormalizeSelection(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
