using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Infrastructure;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
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

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Create()
    {
        var model = new ProductEditViewModel();
        await PopulateFormOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
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
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
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
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (product is null)
        {
            return NotFound();
        }

        var paths = product.Mappings
            .Select(mapping =>
            {
                var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
                var domain = mapping.TrmComponent?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;

                return new ProductDependencyPathViewModel
                {
                    Status = mapping.MappingStatus.ToString(),
                    DomainLabel = domain is null ? "-" : $"{domain.Code} {domain.Name}",
                    CapabilityLabel = capability is null ? "-" : $"{capability.Code} {capability.Name}",
                    ComponentLabel = mapping.TrmComponent?.DisplayLabel ?? "-"
                };
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

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
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

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> BulkEdit(int[]? selectedIds, string? returnSearch = null, string[]? returnOwners = null, string? returnLifecycleStatus = null)
    {
        var normalizedProductIds = NormalizeIds(selectedIds);
        var normalizedReturnOwners = NormalizeSelections(returnOwners);
        var normalizedReturnLifecycleStatus = NormalizeSelection(returnLifecycleStatus);

        if (normalizedProductIds.Count == 0)
        {
            TempData["ProductsErrorMessage"] = "Select one or more products before opening bulk edit.";
            return Redirect(BuildIndexUrl(returnSearch, normalizedReturnOwners, normalizedReturnLifecycleStatus));
        }

        var model = new ProductBulkEditViewModel
        {
            SelectedProductIds = normalizedProductIds,
            ReturnSearch = returnSearch,
            ReturnOwners = normalizedReturnOwners,
            ReturnLifecycleStatus = normalizedReturnLifecycleStatus
        };

        await PopulateBulkEditModelAsync(model);
        if (model.SelectedProducts.Count == 0)
        {
            TempData["ProductsErrorMessage"] = "The selected products could not be found.";
            return Redirect(BuildIndexUrl(model.ReturnSearch, model.ReturnOwners, model.ReturnLifecycleStatus));
        }

        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
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

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEdit(ProductBulkEditViewModel input)
    {
        input.SelectedProductIds = NormalizeIds(input.SelectedProductIds);
        input.ReturnOwners = NormalizeSelections(input.ReturnOwners);
        input.Owners = NormalizeSelections(input.Owners);
        input.OwnerUpdateMode = NormalizeOwnerUpdateMode(input.OwnerUpdateMode);
        input.Vendor = NormalizeSelection(input.Vendor);
        input.ReturnLifecycleStatus = NormalizeSelection(input.ReturnLifecycleStatus);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);

        var products = await dbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .Where(x => input.SelectedProductIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync();

        input.SelectedProductIds = products.Select(x => x.Id).ToList();
        input.SelectedProducts = products
            .Select(BuildBulkEditSelection)
            .ToList();

        if (products.Count == 0)
        {
            TempData["ProductsErrorMessage"] = "The selected products could not be found.";
            return Redirect(BuildIndexUrl(input.ReturnSearch, input.ReturnOwners, input.ReturnLifecycleStatus));
        }

        if (!ModelState.IsValid)
        {
            await PopulateBulkEditOptionsAsync(input);
            return View(input);
        }

        var appliedFields = BuildAppliedFieldList(input);
        var updatedCount = 0;
        var updateTimestamp = DateTime.UtcNow;

        foreach (var productToUpdate in products)
        {
            var changed = false;

            if (input.ApplyVendor && !string.Equals(productToUpdate.Vendor, input.Vendor, StringComparison.Ordinal))
            {
                productToUpdate.Vendor = input.Vendor;
                changed = true;
            }

            if (input.ApplyLifecycleStatus &&
                !string.Equals(productToUpdate.LifecycleStatus, input.LifecycleStatus, StringComparison.Ordinal))
            {
                productToUpdate.LifecycleStatus = input.LifecycleStatus;
                changed = true;
            }

            if (input.ApplyOwners)
            {
                var targetOwners = ResolveBulkOwnerSelection(productToUpdate, input);
                if (!OwnerSelectionsMatch(productToUpdate, targetOwners))
                {
                    SynchronizeOwners(productToUpdate, targetOwners);
                    changed = true;
                }
            }

            if (!changed)
            {
                continue;
            }

            productToUpdate.UpdatedUtc = updateTimestamp;
            updatedCount++;
        }

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Product",
            "BulkUpdate",
            nameof(ProductCatalogItem),
            null,
            $"Bulk updated {updatedCount} of {products.Count} selected product(s).",
            $"Fields: {string.Join(", ", appliedFields)}. Products: {string.Join(", ", products.Select(x => x.Name))}.");

        TempData["ProductsStatusMessage"] = $"Updated {updatedCount} of {products.Count} selected product(s).";
        return Redirect(BuildIndexUrl(input.ReturnSearch, input.ReturnOwners, input.ReturnLifecycleStatus));
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
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

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
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

    private async Task PopulateBulkEditModelAsync(ProductBulkEditViewModel model)
    {
        var products = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .Where(x => model.SelectedProductIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ToListAsync();

        model.SelectedProductIds = products.Select(x => x.Id).ToList();
        model.SelectedProducts = products
            .Select(BuildBulkEditSelection)
            .ToList();

        await PopulateBulkEditOptionsAsync(model);
    }

    private async Task PopulateBulkEditOptionsAsync(ProductBulkEditViewModel model)
    {
        model.OwnerOptions = await configurableFieldService.GetMultiSelectListAsync(
            ConfigurableFieldNames.Owner,
            model.Owners);
        model.LifecycleStatusOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            model.LifecycleStatus,
            "Clear lifecycle status");
    }

    private static string? NormalizeSelection(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeOwnerUpdateMode(string? value) =>
        string.Equals(value, ProductBulkOwnerUpdateModes.Append, StringComparison.OrdinalIgnoreCase)
            ? ProductBulkOwnerUpdateModes.Append
            : ProductBulkOwnerUpdateModes.Replace;

    private static string BuildIndexUrl(string? search, IReadOnlyCollection<string>? owners, string? lifecycleStatus)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (!string.IsNullOrWhiteSpace(lifecycleStatus))
        {
            parameters.Add($"lifecycleStatus={Uri.EscapeDataString(lifecycleStatus)}");
        }

        if (owners is not null)
        {
            foreach (var owner in owners.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                parameters.Add($"owners={Uri.EscapeDataString(owner)}");
            }
        }

        return parameters.Count == 0
            ? "/Products"
            : $"/Products?{string.Join("&", parameters)}";
    }

    private static List<int> NormalizeIds(IEnumerable<int>? ids)
    {
        var normalized = new List<int>();
        if (ids is null)
        {
            return normalized;
        }

        foreach (var id in ids)
        {
            if (id <= 0 || normalized.Contains(id))
            {
                continue;
            }

            normalized.Add(id);
        }

        return normalized;
    }

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

    private static ProductBulkEditSelectionViewModel BuildBulkEditSelection(ProductCatalogItem product) =>
        new()
        {
            Id = product.Id,
            Name = product.Name,
            Vendor = product.Vendor,
            LifecycleStatus = product.LifecycleStatus,
            Owners = product.OwnerValues
        };

    private static IReadOnlyList<string> BuildAppliedFieldList(ProductBulkEditViewModel input)
    {
        var appliedFields = new List<string>();
        if (input.ApplyVendor)
        {
            appliedFields.Add("Vendor");
        }

        if (input.ApplyOwners)
        {
            appliedFields.Add(input.OwnerUpdateMode == ProductBulkOwnerUpdateModes.Append
                ? "Owners (append)"
                : "Owners (replace)");
        }

        if (input.ApplyLifecycleStatus)
        {
            appliedFields.Add("Lifecycle status");
        }

        return appliedFields;
    }

    private static bool OwnerSelectionsMatch(ProductCatalogItem product, IReadOnlyCollection<string> selectedOwners)
    {
        if (product.Owners.Count != selectedOwners.Count)
        {
            return false;
        }

        return product.Owners.All(owner =>
            selectedOwners.Any(selected => string.Equals(selected, owner.OwnerValue, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> ResolveBulkOwnerSelection(ProductCatalogItem product, ProductBulkEditViewModel input)
    {
        if (input.OwnerUpdateMode != ProductBulkOwnerUpdateModes.Append)
        {
            return input.Owners;
        }

        var mergedOwners = product.OwnerValues.ToList();
        foreach (var owner in input.Owners)
        {
            if (mergedOwners.Contains(owner, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            mergedOwners.Add(owner);
        }

        return mergedOwners;
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
