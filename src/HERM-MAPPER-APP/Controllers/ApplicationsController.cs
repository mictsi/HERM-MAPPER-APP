using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class ApplicationsController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    HermDrilldownService drilldownService) : Controller
{
    private const int MinimumMappingRowCount = 8;

    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductCatalogItem)
            .AsSplitQuery()
            .AsQueryable();

        var likePattern = SearchPattern.CreateContainsPattern(search);
        if (likePattern is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Name, likePattern) ||
                (x.Description != null && EF.Functions.Like(x.Description, likePattern)) ||
                (x.Notes != null && EF.Functions.Like(x.Notes, likePattern)) ||
                x.Mappings.Any(mapping =>
                    EF.Functions.Like(mapping.ArmComponent!.Code, likePattern) ||
                    EF.Functions.Like(mapping.ArmComponent.Name, likePattern) ||
                    EF.Functions.Like(mapping.ProductCatalogItem!.Name, likePattern) ||
                    (mapping.ProductCatalogItem.Vendor != null && EF.Functions.Like(mapping.ProductCatalogItem.Vendor, likePattern))));
        }

        var applications = await query
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(new ApplicationsIndexViewModel
        {
            Search = search,
            Applications = applications
                .Select(BuildIndexRow)
                .ToList()
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Create()
    {
        var model = new ApplicationEditViewModel();
        EnsureMappingRows(model.MappingRows);
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApplicationEditViewModel input)
    {
        NormalizeInput(input);
        var normalizedMappings = await ValidateMappingsAsync(input);
        if (!ModelState.IsValid || normalizedMappings is null)
        {
            EnsureMappingRows(input.MappingRows);
            await PopulateOptionsAsync(input);
            return View(input);
        }

        var application = new ApplicationCatalogItem
        {
            Name = input.Name,
            Description = NormalizeSelection(input.Description),
            Notes = NormalizeSelection(input.Notes),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        foreach (var mapping in normalizedMappings)
        {
            application.Mappings.Add(new ApplicationCatalogItemMapping
            {
                ArmComponentId = mapping.ArmComponentId,
                ProductCatalogItemId = mapping.ProductCatalogItemId,
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                CreatedUtc = DateTime.UtcNow
            });
        }

        dbContext.ApplicationCatalogItems.Add(application);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Application",
            "Create",
            nameof(ApplicationCatalogItem),
            application.Id,
            $"Created application {application.Name}.",
            $"ARM/product mappings: {application.Mappings.Count}.");

        TempData["ApplicationsStatusMessage"] = $"Created application {application.Name}.";
        return RedirectToAction(nameof(Details), new { id = application.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var model = await drilldownService.BuildApplicationDetailsAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["StatusMessage"] = TempData["ApplicationsStatusMessage"] as string;
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Edit(int id)
    {
        var application = await dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (application is null)
        {
            return NotFound();
        }

        var model = new ApplicationEditViewModel
        {
            Id = application.Id,
            Name = application.Name,
            Description = application.Description,
            Notes = application.Notes,
            MappingRows = application.Mappings
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.Id)
                .Select(x => new ApplicationMappingRowInputViewModel
                {
                    ArmComponentId = x.ArmComponentId,
                    ProductCatalogItemId = x.ProductCatalogItemId,
                    IsPrimary = x.IsPrimary,
                    Notes = x.Notes
                })
                .ToList()
        };

        EnsureMappingRows(model.MappingRows);
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ApplicationEditViewModel input)
    {
        var application = await dbContext.ApplicationCatalogItems
            .Include(x => x.Mappings)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (application is null)
        {
            return NotFound();
        }

        input.Id = id;
        NormalizeInput(input);
        var normalizedMappings = await ValidateMappingsAsync(input);
        if (!ModelState.IsValid || normalizedMappings is null)
        {
            EnsureMappingRows(input.MappingRows);
            await PopulateOptionsAsync(input);
            return View(input);
        }

        application.Name = input.Name;
        application.Description = NormalizeSelection(input.Description);
        application.Notes = NormalizeSelection(input.Notes);
        application.UpdatedUtc = DateTime.UtcNow;

        dbContext.ApplicationCatalogItemMappings.RemoveRange(application.Mappings);
        application.Mappings.Clear();

        foreach (var mapping in normalizedMappings)
        {
            application.Mappings.Add(new ApplicationCatalogItemMapping
            {
                ArmComponentId = mapping.ArmComponentId,
                ProductCatalogItemId = mapping.ProductCatalogItemId,
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                CreatedUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Application",
            "Update",
            nameof(ApplicationCatalogItem),
            application.Id,
            $"Updated application {application.Name}.",
            $"ARM/product mappings: {application.Mappings.Count}.");

        TempData["ApplicationsStatusMessage"] = $"Updated application {application.Name}.";
        return RedirectToAction(nameof(Details), new { id = application.Id });
    }

    private async Task PopulateOptionsAsync(ApplicationEditViewModel model)
    {
        model.ArmComponentOptions = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem(
                $"{x.Code} {x.Name} ({x.ParentCapability!.ParentDomain!.Code}/{x.ParentCapability.Code})",
                x.Id.ToString()))
            .ToListAsync();

        model.ProductOptions = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(
                string.IsNullOrWhiteSpace(x.Vendor) ? x.Name : $"{x.Name} ({x.Vendor})",
                x.Id.ToString()))
            .ToListAsync();
    }

    private async Task<List<NormalizedApplicationMappingRow>?> ValidateMappingsAsync(ApplicationEditViewModel input)
    {
        var normalizedRows = new List<NormalizedApplicationMappingRow>();

        for (var index = 0; index < input.MappingRows.Count; index++)
        {
            var row = input.MappingRows[index];
            row.Notes = NormalizeSelection(row.Notes);

            var hasArmComponent = row.ArmComponentId.HasValue;
            var hasProduct = row.ProductCatalogItemId.HasValue;
            if (!hasArmComponent && !hasProduct && string.IsNullOrWhiteSpace(row.Notes) && !row.IsPrimary)
            {
                continue;
            }

            if (!hasArmComponent)
            {
                ModelState.AddModelError($"MappingRows[{index}].ArmComponentId", "Choose an ARM component.");
            }

            if (!hasProduct)
            {
                ModelState.AddModelError($"MappingRows[{index}].ProductCatalogItemId", "Choose a supporting product.");
            }

            if (!hasArmComponent || !hasProduct)
            {
                continue;
            }

            normalizedRows.Add(new NormalizedApplicationMappingRow(
                row.ArmComponentId!.Value,
                row.ProductCatalogItemId!.Value,
                row.IsPrimary,
                row.Notes));
        }

        if (normalizedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Add at least one ARM component to product mapping.");
            return null;
        }

        var armComponentIds = normalizedRows.Select(x => x.ArmComponentId).Distinct().ToList();
        var validArmComponentIds = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && armComponentIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        var productIds = normalizedRows.Select(x => x.ProductCatalogItemId).Distinct().ToList();
        var validProductIds = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted && productIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var invalidArmComponentId in armComponentIds.Except(validArmComponentIds))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"ARM component {invalidArmComponentId} could not be found.");
        }

        foreach (var invalidProductId in productIds.Except(validProductIds))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"Product {invalidProductId} could not be found.");
        }

        var duplicateMappings = normalizedRows
            .GroupBy(x => new { x.ArmComponentId, x.ProductCatalogItemId })
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateMappings.Count != 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Duplicate ARM component and product combinations are not allowed.");
        }

        return ModelState.IsValid ? normalizedRows : null;
    }

    private static void NormalizeInput(ApplicationEditViewModel input)
    {
        input.Description = NormalizeSelection(input.Description);
        input.Notes = NormalizeSelection(input.Notes);
        input.MappingRows ??= [];
    }

    private static void EnsureMappingRows(List<ApplicationMappingRowInputViewModel> mappingRows)
    {
        while (mappingRows.Count < MinimumMappingRowCount)
        {
            mappingRows.Add(new ApplicationMappingRowInputViewModel());
        }
    }

    private static string? NormalizeSelection(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static ApplicationIndexRowViewModel BuildIndexRow(ApplicationCatalogItem application) =>
        new()
        {
            Id = application.Id,
            Name = application.Name,
            Description = application.Description,
            ArmComponentCount = application.Mappings
                .Select(x => x.ArmComponentId)
                .Distinct()
                .Count(),
            ProductCount = application.Mappings
                .Select(x => x.ProductCatalogItemId)
                .Distinct()
                .Count(),
            ResolvedPathCount = application.Mappings
                .Sum(x => Math.Max(1, x.ProductCatalogItem?.Mappings.Count ?? 0)),
            UpdatedUtc = application.UpdatedUtc
        };

    private sealed record NormalizedApplicationMappingRow(
        int ArmComponentId,
        int ProductCatalogItemId,
        bool IsPrimary,
        string? Notes);
}
