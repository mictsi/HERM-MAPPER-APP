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
    public async Task<IActionResult> Index(string? search, string[]? owners = null, string? lifecycleStatus = null)
    {
        var query = dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .AsSplitQuery()
            .AsQueryable();

        var selectedOwners = NormalizeSelections(owners);
        lifecycleStatus = NormalizeSelection(lifecycleStatus);
        var selectedOwnersLower = selectedOwners.Select(x => x.ToLowerInvariant()).ToList();

        var likePattern = SearchPattern.CreateContainsPattern(search);
        if (likePattern is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Name, likePattern) ||
                (x.Vendor != null && EF.Functions.Like(x.Vendor, likePattern)) ||
                (x.Version != null && EF.Functions.Like(x.Version, likePattern)) ||
                x.Owners.Any(owner => EF.Functions.Like(owner.OwnerValue, likePattern)) ||
                (x.LifecycleStatus != null && EF.Functions.Like(x.LifecycleStatus, likePattern)) ||
                (x.Description != null && EF.Functions.Like(x.Description, likePattern)) ||
                (x.Notes != null && EF.Functions.Like(x.Notes, likePattern)));
        }

        if (selectedOwnersLower.Count != 0)
        {
            query = query.Where(x => x.Owners.Any(owner => selectedOwnersLower.Contains(owner.OwnerValue.ToLower())));
        }

        if (lifecycleStatus is not null)
        {
            query = query.Where(x => x.LifecycleStatus != null && x.LifecycleStatus.ToLower() == lifecycleStatus.ToLower());
        }

        var model = new ProductsIndexViewModel
        {
            Search = search,
            SelectedOwners = selectedOwners,
            LifecycleStatus = lifecycleStatus,
            AvailableOwners = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.Owner),
            LifecycleStatuses = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus),
            Products = await query.OrderBy(x => x.Name).ToListAsync()
        };

        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var model = new ProductEditViewModel();
        await PopulateFormOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductEditViewModel input)
    {
        input.Owners = NormalizeSelections(input.Owners);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);

        if (!ModelState.IsValid)
        {
            await PopulateFormOptionsAsync(input);
            return View(input);
        }

        var product = new ProductCatalogItem
        {
            Name = input.Name,
            Vendor = input.Vendor,
            Version = input.Version,
            LifecycleStatus = input.LifecycleStatus,
            Description = input.Description,
            Notes = input.Notes,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        SynchronizeOwners(product, input.Owners);

        dbContext.ProductCatalogItems.Add(product);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Product",
            "Create",
            nameof(ProductCatalogItem),
            product.Id,
            $"Created product {product.Name}.");

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmCapability)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .AsSplitQuery()
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
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (product is null)
        {
            return NotFound();
        }

        var model = ProductEditViewModel.FromProduct(product);
        await PopulateFormOptionsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductEditViewModel input)
    {
        var product = await dbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (product is null)
        {
            return NotFound();
        }

        input.Id = id;
        input.Owners = NormalizeSelections(input.Owners);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);

        if (!ModelState.IsValid)
        {
            await PopulateFormOptionsAsync(input);
            return View(input);
        }

        product.Name = input.Name;
        product.Vendor = input.Vendor;
        product.Version = input.Version;
        product.LifecycleStatus = input.LifecycleStatus;
        product.Description = input.Description;
        product.Notes = input.Notes;
        product.UpdatedUtc = DateTime.UtcNow;
        SynchronizeOwners(product, input.Owners);

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
            .Include(x => x.Owners)
            .Include(x => x.Mappings)
            .AsSplitQuery()
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

    private async Task PopulateFormOptionsAsync(ProductEditViewModel model)
    {
        model.OwnerOptions = await configurableFieldService.GetMultiSelectListAsync(
            ConfigurableFieldNames.Owner,
            model.Owners);
        model.LifecycleStatusOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            model.LifecycleStatus);
    }

    private static string? NormalizeSelection(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SynchronizeOwners(ProductCatalogItem product, IReadOnlyCollection<string> selectedOwners)
    {
        var existingOwners = product.Owners.ToList();

        foreach (var owner in existingOwners.Where(owner =>
                     selectedOwners.All(selected => !string.Equals(selected, owner.OwnerValue, StringComparison.OrdinalIgnoreCase))))
        {
            product.Owners.Remove(owner);
            dbContext.ProductCatalogItemOwners.Remove(owner);
        }

        foreach (var owner in selectedOwners.Where(selected =>
                     existingOwners.All(existing => !string.Equals(existing.OwnerValue, selected, StringComparison.OrdinalIgnoreCase))))
        {
            product.Owners.Add(new ProductCatalogItemOwner
            {
                OwnerValue = owner
            });
        }
    }

    private static List<string> NormalizeSelections(IEnumerable<string>? values)
    {
        var normalized = new List<string>();
        if (values is null)
        {
            return normalized;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (normalized.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized;
    }
}
