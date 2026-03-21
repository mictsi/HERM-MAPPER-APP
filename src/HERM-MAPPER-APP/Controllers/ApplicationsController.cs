using System.Globalization;
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
    private const int MinimumMappingRowCount = 1;

    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.ProductCatalogItem)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
            .ThenInclude(x => x!.TrmComponent)
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
                    (mapping.ProductMapping != null &&
                        (EF.Functions.Like(mapping.ProductMapping.ProductCatalogItem!.Name, likePattern) ||
                         (mapping.ProductMapping.ProductCatalogItem.Vendor != null &&
                          EF.Functions.Like(mapping.ProductMapping.ProductCatalogItem.Vendor, likePattern)) ||
                         (mapping.ProductMapping.TrmComponent != null &&
                            (EF.Functions.Like(mapping.ProductMapping.TrmComponent.Code, likePattern) ||
                             EF.Functions.Like(mapping.ProductMapping.TrmComponent.Name, likePattern)))))));
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
                ProductMappingId = mapping.ProductMappingId,
                ProductCatalogItemId = mapping.ProductCatalogItemId,
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
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var model = await drilldownService.BuildApplicationDetailsAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["StatusMessage"] = TempData["ApplicationsStatusMessage"] as string;
        return View(model);
    }

    public async Task<IActionResult> AllDependencies(CancellationToken cancellationToken)
    {
        var model = new HierarchyDiagramPageViewModel
        {
            Title = "All applications",
            Eyebrow = "Hierarchy",
            Heading = "All application dependencies",
            Description = "Explore the full application dependency tree across ARM and TRM with the same left-to-right view used on each application page.",
            BackLabel = "Back to applications",
            BackAction = nameof(Index),
            HierarchyRoot = await drilldownService.BuildAllApplicationsHierarchyAsync(cancellationToken),
            EmptyTitle = "No application dependency map yet",
            EmptyBody = "Create applications and connect them to ARM components and TRM product mappings to generate the full dependency tree.",
            Note = "Drag to pan and use the mouse wheel to zoom. The tree reads from left to right, while product endpoints stay out of the diagram to keep it readable."
        };

        return View("~/Views/Shared/HierarchyDiagramPage.cshtml", model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Edit(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var application = await dbContext.ApplicationCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ProductMapping)
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
                .OrderBy(x => x.Id)
                .Select(x => new ApplicationMappingRowInputViewModel
                {
                    ArmComponentId = x.ArmComponentId,
                    ProductCatalogItemId = x.ProductCatalogItemId,
                    TrmComponentId = x.ProductMapping?.TrmComponentId
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
                ProductMappingId = mapping.ProductMappingId,
                ProductCatalogItemId = mapping.ProductCatalogItemId,
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
                x.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync();

        var productMappings = await dbContext.ProductMappings
            .AsNoTracking()
            .Where(x =>
                x.ProductCatalogItem != null &&
                !x.ProductCatalogItem.IsDeleted &&
                x.TrmComponentId != null)
            .Include(x => x.ProductCatalogItem)
            .Include(x => x.TrmComponent)
            .ThenInclude(x => x!.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .OrderBy(x => x.ProductCatalogItem!.Name)
            .ThenBy(x => x.TrmComponent!.Code)
            .ToListAsync();

        model.ProductOptions = productMappings
            .GroupBy(x => new
            {
                x.ProductCatalogItemId,
                Label = string.IsNullOrWhiteSpace(x.ProductCatalogItem!.Vendor)
                    ? x.ProductCatalogItem.Name
                    : $"{x.ProductCatalogItem.Name} ({x.ProductCatalogItem.Vendor})"
            })
            .OrderBy(group => group.Key.Label)
            .Select(group => new SelectListItem(group.Key.Label, group.Key.ProductCatalogItemId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.TrmComponentOptions = productMappings
            .GroupBy(x => new
            {
                TrmComponentId = x.TrmComponentId!.Value,
                Label = $"{x.TrmComponent!.Code} {x.TrmComponent.Name}"
            })
            .OrderBy(group => group.Key.Label)
            .Select(group => new SelectListItem(group.Key.Label, group.Key.TrmComponentId.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ProductTrmMappingOptions = productMappings
            .Select(x => new ApplicationProductTrmMappingOptionViewModel
            {
                ProductCatalogItemId = x.ProductCatalogItemId,
                ProductLabel = string.IsNullOrWhiteSpace(x.ProductCatalogItem!.Vendor)
                    ? x.ProductCatalogItem.Name
                    : $"{x.ProductCatalogItem.Name} ({x.ProductCatalogItem.Vendor})",
                TrmComponentId = x.TrmComponentId!.Value,
                TrmComponentLabel = $"{x.TrmComponent!.Code} {x.TrmComponent.Name}",
                ProductMappingId = x.Id
            })
            .ToList();
    }

    private async Task<List<NormalizedApplicationMappingRow>?> ValidateMappingsAsync(ApplicationEditViewModel input)
    {
        var normalizedRows = new List<NormalizedApplicationMappingRow>();

        for (var index = 0; index < input.MappingRows.Count; index++)
        {
            var row = input.MappingRows[index];

            var hasArmComponent = row.ArmComponentId.HasValue;
            var hasProduct = row.ProductCatalogItemId.HasValue;
            var hasTrmComponent = row.TrmComponentId.HasValue;
            if (!hasArmComponent && !hasProduct && !hasTrmComponent)
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
                index,
                row.ArmComponentId!.Value,
                row.ProductCatalogItemId!.Value,
                row.TrmComponentId));
        }

        if (normalizedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Add at least one ARM component to TRM product mapping.");
            return null;
        }

        var armComponentIds = normalizedRows.Select(x => x.ArmComponentId).Distinct().ToList();
        var validArmComponentIds = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && armComponentIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        var productIds = normalizedRows.Select(x => x.ProductCatalogItemId).Distinct().ToList();
        var productMappings = await dbContext.ProductMappings
            .AsNoTracking()
            .Where(x =>
                productIds.Contains(x.ProductCatalogItemId) &&
                x.ProductCatalogItem != null &&
                !x.ProductCatalogItem.IsDeleted &&
                x.TrmComponentId != null)
            .Select(x => new
            {
                x.Id,
                x.ProductCatalogItemId,
                TrmComponentId = x.TrmComponentId!.Value
            })
            .ToListAsync();

        foreach (var invalidArmComponentId in armComponentIds.Except(validArmComponentIds))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"ARM component {invalidArmComponentId} could not be found.");
        }

        foreach (var invalidProductId in productIds.Except(productMappings.Select(x => x.ProductCatalogItemId).Distinct()))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"Product {invalidProductId} could not be resolved to a TRM mapping.");
        }

        for (var index = 0; index < normalizedRows.Count; index++)
        {
            var row = normalizedRows[index];
            var matchingMappings = productMappings
                .Where(x => x.ProductCatalogItemId == row.ProductCatalogItemId)
                .ToList();

            if (matchingMappings.Count == 0)
            {
                continue;
            }

            var selectedMapping = matchingMappings.Count == 1 ? matchingMappings[0] : null;
            if (row.TrmComponentId.HasValue)
            {
                selectedMapping = matchingMappings.FirstOrDefault(x => x.TrmComponentId == row.TrmComponentId.Value);
            }

            if (row.TrmComponentId.HasValue && selectedMapping is null)
            {
                ModelState.AddModelError(
                    $"MappingRows[{row.RowIndex}].TrmComponentId",
                    "Choose a TRM component that matches the selected product.");
                continue;
            }

            if (!row.TrmComponentId.HasValue && matchingMappings.Count > 1)
            {
                ModelState.AddModelError(
                    $"MappingRows[{row.RowIndex}].TrmComponentId",
                    "Choose the TRM component for the selected product.");
                continue;
            }

            if (selectedMapping is not null)
            {
                normalizedRows[index] = row with
                {
                    TrmComponentId = selectedMapping.TrmComponentId,
                    ProductMappingId = selectedMapping.Id
                };
            }
        }

        var duplicateMappings = normalizedRows
            .Where(x => x.ProductMappingId.HasValue)
            .GroupBy(x => new { x.ArmComponentId, ProductMappingId = x.ProductMappingId!.Value })
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateMappings.Count != 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Duplicate ARM component, product, and TRM component combinations are not allowed.");
        }

        if (!ModelState.IsValid)
        {
            return null;
        }

        return normalizedRows
            .ToList();
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
            ResolvedPathCount = application.Mappings.Count,
            UpdatedUtc = application.UpdatedUtc
        };

    private sealed record NormalizedApplicationMappingRow(
        int RowIndex,
        int ArmComponentId,
        int ProductCatalogItemId,
        int? TrmComponentId)
    {
        public int? ProductMappingId { get; init; }
    }
}
