using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class CapabilitiesController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    HermDrilldownService drilldownService) : Controller
{
    private const int MinimumMappingRowCount = 8;

    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.BrmComponent)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .AsSplitQuery()
            .AsQueryable();

        var likePattern = HERMMapperApp.Infrastructure.SearchPattern.CreateContainsPattern(search);
        if (likePattern is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Name, likePattern) ||
                (x.Description != null && EF.Functions.Like(x.Description, likePattern)) ||
                (x.Notes != null && EF.Functions.Like(x.Notes, likePattern)) ||
                x.Mappings.Any(mapping =>
                    EF.Functions.Like(mapping.BrmComponent!.Code, likePattern) ||
                    EF.Functions.Like(mapping.BrmComponent.Name, likePattern) ||
                    EF.Functions.Like(mapping.ArmComponent!.Code, likePattern) ||
                    EF.Functions.Like(mapping.ArmComponent.Name, likePattern)));
        }

        var capabilities = await query
            .OrderBy(x => x.Name)
            .ToListAsync();

        var linkedApplicationCountByCapabilityId = capabilities.ToDictionary(capability => capability.Id, _ => 0);
        var linkedProductCountByCapabilityId = capabilities.ToDictionary(capability => capability.Id, _ => 0);

        var allArmComponentIds = capabilities
            .SelectMany(x => x.Mappings)
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        if (allArmComponentIds.Count != 0)
        {
            var applicationMappings = await dbContext.ApplicationCatalogItemMappings
                .AsNoTracking()
                .Where(x => allArmComponentIds.Contains(x.ArmComponentId))
                .Select(x => new { x.ApplicationCatalogItemId, x.ArmComponentId, x.ProductCatalogItemId })
                .ToListAsync();

            foreach (var capability in capabilities)
            {
                var armIds = capability.Mappings.Select(x => x.ArmComponentId).Distinct().ToHashSet();
                var matchingApplicationMappings = applicationMappings
                    .Where(x => armIds.Contains(x.ArmComponentId))
                    .ToList();

                linkedApplicationCountByCapabilityId[capability.Id] = matchingApplicationMappings
                    .Select(x => x.ApplicationCatalogItemId)
                    .Distinct()
                    .Count();

                linkedProductCountByCapabilityId[capability.Id] = matchingApplicationMappings
                    .Select(x => x.ProductCatalogItemId)
                    .Distinct()
                    .Count();
            }
        }

        return View(new CapabilitiesIndexViewModel
        {
            Search = search,
            Capabilities = capabilities
                .Select(capability => BuildIndexRow(
                    capability,
                    linkedApplicationCountByCapabilityId.GetValueOrDefault(capability.Id),
                    linkedProductCountByCapabilityId.GetValueOrDefault(capability.Id)))
                .ToList()
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Create()
    {
        var model = new CapabilityEditViewModel();
        EnsureMappingRows(model.MappingRows);
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CapabilityEditViewModel input)
    {
        NormalizeInput(input);
        var normalizedMappings = await ValidateMappingsAsync(input);
        if (!ModelState.IsValid || normalizedMappings is null)
        {
            EnsureMappingRows(input.MappingRows);
            await PopulateOptionsAsync(input);
            return View(input);
        }

        var capability = new BusinessCapabilityCatalogItem
        {
            Name = input.Name,
            Description = NormalizeSelection(input.Description),
            Notes = NormalizeSelection(input.Notes),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        foreach (var mapping in normalizedMappings)
        {
            capability.Mappings.Add(new BusinessCapabilityCatalogItemMapping
            {
                BrmComponentId = mapping.BrmComponentId,
                ArmComponentId = mapping.ArmComponentId,
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                CreatedUtc = DateTime.UtcNow
            });
        }

        dbContext.BusinessCapabilityCatalogItems.Add(capability);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Capability",
            "Create",
            nameof(BusinessCapabilityCatalogItem),
            capability.Id,
            $"Created capability {capability.Name}.",
            $"BRM/ARM mappings: {capability.Mappings.Count}.");

        TempData["CapabilitiesStatusMessage"] = $"Created capability {capability.Name}.";
        return RedirectToAction(nameof(Details), new { id = capability.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var model = await drilldownService.BuildCapabilityDetailsAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["StatusMessage"] = TempData["CapabilitiesStatusMessage"] as string;
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Edit(int id)
    {
        var capability = await dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (capability is null)
        {
            return NotFound();
        }

        var model = new CapabilityEditViewModel
        {
            Id = capability.Id,
            Name = capability.Name,
            Description = capability.Description,
            Notes = capability.Notes,
            MappingRows = capability.Mappings
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.Id)
                .Select(x => new CapabilityMappingRowInputViewModel
                {
                    BrmComponentId = x.BrmComponentId,
                    ArmComponentId = x.ArmComponentId,
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
    public async Task<IActionResult> Edit(int id, CapabilityEditViewModel input)
    {
        var capability = await dbContext.BusinessCapabilityCatalogItems
            .Include(x => x.Mappings)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (capability is null)
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

        capability.Name = input.Name;
        capability.Description = NormalizeSelection(input.Description);
        capability.Notes = NormalizeSelection(input.Notes);
        capability.UpdatedUtc = DateTime.UtcNow;

        dbContext.BusinessCapabilityCatalogItemMappings.RemoveRange(capability.Mappings);
        capability.Mappings.Clear();

        foreach (var mapping in normalizedMappings)
        {
            capability.Mappings.Add(new BusinessCapabilityCatalogItemMapping
            {
                BrmComponentId = mapping.BrmComponentId,
                ArmComponentId = mapping.ArmComponentId,
                IsPrimary = mapping.IsPrimary,
                Notes = mapping.Notes,
                CreatedUtc = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Capability",
            "Update",
            nameof(BusinessCapabilityCatalogItem),
            capability.Id,
            $"Updated capability {capability.Name}.",
            $"BRM/ARM mappings: {capability.Mappings.Count}.");

        TempData["CapabilitiesStatusMessage"] = $"Updated capability {capability.Name}.";
        return RedirectToAction(nameof(Details), new { id = capability.Id });
    }

    private async Task PopulateOptionsAsync(CapabilityEditViewModel model)
    {
        model.BrmComponentOptions = await dbContext.BrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem(
                $"{x.Code} {x.Name} ({x.ParentCapability!.ParentDomain!.Code}/{x.ParentCapability.Code})",
                x.Id.ToString()))
            .ToListAsync();

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
    }

    private async Task<List<NormalizedCapabilityMappingRow>?> ValidateMappingsAsync(CapabilityEditViewModel input)
    {
        var normalizedRows = new List<NormalizedCapabilityMappingRow>();

        for (var index = 0; index < input.MappingRows.Count; index++)
        {
            var row = input.MappingRows[index];
            row.Notes = NormalizeSelection(row.Notes);

            var hasBrmComponent = row.BrmComponentId.HasValue;
            var hasArmComponent = row.ArmComponentId.HasValue;
            if (!hasBrmComponent && !hasArmComponent && string.IsNullOrWhiteSpace(row.Notes) && !row.IsPrimary)
            {
                continue;
            }

            if (!hasBrmComponent)
            {
                ModelState.AddModelError($"MappingRows[{index}].BrmComponentId", "Choose a BRM capability.");
            }

            if (!hasArmComponent)
            {
                ModelState.AddModelError($"MappingRows[{index}].ArmComponentId", "Choose a supporting ARM component.");
            }

            if (!hasBrmComponent || !hasArmComponent)
            {
                continue;
            }

            normalizedRows.Add(new NormalizedCapabilityMappingRow(
                row.BrmComponentId!.Value,
                row.ArmComponentId!.Value,
                row.IsPrimary,
                row.Notes));
        }

        if (normalizedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Add at least one BRM to ARM mapping.");
            return null;
        }

        var brmComponentIds = normalizedRows.Select(x => x.BrmComponentId).Distinct().ToList();
        var validBrmComponentIds = await dbContext.BrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && brmComponentIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        var armComponentIds = normalizedRows.Select(x => x.ArmComponentId).Distinct().ToList();
        var validArmComponentIds = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted && armComponentIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var invalidBrmComponentId in brmComponentIds.Except(validBrmComponentIds))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"BRM capability {invalidBrmComponentId} could not be found.");
        }

        foreach (var invalidArmComponentId in armComponentIds.Except(validArmComponentIds))
        {
            ModelState.AddModelError(nameof(input.MappingRows), $"ARM component {invalidArmComponentId} could not be found.");
        }

        var duplicateMappings = normalizedRows
            .GroupBy(x => new { x.BrmComponentId, x.ArmComponentId })
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateMappings.Count != 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Duplicate BRM and ARM combinations are not allowed.");
        }

        return ModelState.IsValid ? normalizedRows : null;
    }

    private static void NormalizeInput(CapabilityEditViewModel input)
    {
        input.Description = NormalizeSelection(input.Description);
        input.Notes = NormalizeSelection(input.Notes);
        input.MappingRows ??= [];
    }

    private static void EnsureMappingRows(List<CapabilityMappingRowInputViewModel> mappingRows)
    {
        while (mappingRows.Count < MinimumMappingRowCount)
        {
            mappingRows.Add(new CapabilityMappingRowInputViewModel());
        }
    }

    private static string? NormalizeSelection(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static CapabilityIndexRowViewModel BuildIndexRow(BusinessCapabilityCatalogItem capability, int applicationCount, int productCount) =>
        new()
        {
            Id = capability.Id,
            Name = capability.Name,
            Description = capability.Description,
            BrmCapabilityCount = capability.Mappings
                .Select(x => x.BrmComponentId)
                .Distinct()
                .Count(),
            ArmComponentCount = capability.Mappings
                .Select(x => x.ArmComponentId)
                .Distinct()
                .Count(),
            ApplicationCount = applicationCount,
            ProductCount = productCount,
            UpdatedUtc = capability.UpdatedUtc
        };

    private sealed record NormalizedCapabilityMappingRow(
        int BrmComponentId,
        int ArmComponentId,
        bool IsPrimary,
        string? Notes);
}
