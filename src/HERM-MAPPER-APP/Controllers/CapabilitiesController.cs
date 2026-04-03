using System.Globalization;
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
    private const int MinimumMappingRowCount = 1;

    public Task<IActionResult> IndexAsync(string? search, int? brmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return Task.FromResult<IActionResult>(BadRequest(ModelState));
        }

        IActionResult result = brmModelId is > 0
            ? RedirectToAction("Details", "BrmModels", new { id = brmModelId.Value })
            : RedirectToAction("Index", "BrmModels");

        return Task.FromResult(result);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> CreateAsync(int? brmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (brmModelId is not > 0)
        {
            return RedirectToAction("Index", "BrmModels");
        }

        var brmModelExists = await dbContext.BrmModels
            .AsNoTracking()
            .AnyAsync(x => x.Id == brmModelId.Value && !x.IsDeleted);
        if (!brmModelExists)
        {
            return RedirectToAction("Index", "BrmModels");
        }

        var model = new CapabilityEditViewModel
        {
            SelectedBrmModelId = brmModelId
        };
        EnsureMappingRows(model.MappingRows);
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync(int brmModelId, CapabilityEditViewModel input)
    {
        if (brmModelId <= 0)
        {
            return RedirectToAction("Index", "BrmModels");
        }

        input.SelectedBrmModelId = brmModelId;
        ModelState.Remove(nameof(input.SelectedBrmModelId));
        NormalizeInput(input);
        var normalizedMappings = await ValidateMappingsAsync(input);
        if (!ModelState.IsValid || normalizedMappings is null)
        {
            EnsureMappingRows(input.MappingRows);
            await PopulateOptionsAsync(input);
            return View(input);
        }

        var brmComponent = await dbContext.BrmComponents
            .AsNoTracking()
            .FirstAsync(x => x.Id == input.SelectedBrmComponentId!.Value);

        var capability = new BusinessCapabilityCatalogItem
        {
            BrmModelId = input.SelectedBrmModelId,
            Name = BuildBrmComponentLabel(brmComponent),
            Description = NormalizeSelection(input.Description),
            Notes = NormalizeSelection(input.Notes),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        foreach (var mapping in normalizedMappings)
        {
            capability.Mappings.Add(new BusinessCapabilityCatalogItemMapping
            {
                BrmComponentId = input.SelectedBrmComponentId!.Value,
                ArmComponentId = mapping.ArmComponentId,
                ArmCapabilityId = mapping.ArmCapabilityId,
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

        TempData["BrmModelsStatusMessage"] = $"Created capability {capability.Name} in {await ResolveBrmModelNameAsync(capability.BrmModelId)}.";
        return RedirectToAction("Details", "BrmModels", new { id = capability.BrmModelId });
    }

    public async Task<IActionResult> DetailsAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var model = await drilldownService.BuildCapabilityDetailsAsync(id);
        if (model is null)
        {
            return NotFound();
        }

        ViewData["StatusMessage"] = TempData["CapabilitiesStatusMessage"] as string;
        return View(model);
    }

    public async Task<IActionResult> AllDependenciesAsync(CancellationToken cancellationToken)
    {
        var model = new HierarchyDiagramPageViewModel
        {
            Title = "All capabilities",
            Eyebrow = "Hierarchy",
            Heading = "All capability dependencies",
            Description = "Explore the full capability drilldown from BRM into ARM, applications, and TRM with the same dependency map settings used on each capability page.",
            BackLabel = "Back to BRM models",
            BackAction = "Index",
            HierarchyRoot = await drilldownService.BuildAllCapabilitiesHierarchyAsync(cancellationToken),
            EmptyTitle = "No capability dependency map yet",
            EmptyBody = "Create capabilities and connect them to ARM components, applications, and TRM mappings to generate the full dependency tree.",
            Note = "Drag to pan and use the mouse wheel to zoom. The tree reads from left to right and now includes connected products where they exist.",
            IncludeProducts = true
        };

        return View("~/Views/Shared/HierarchyDiagramPage.cshtml", model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> EditAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var capability = await dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .FirstOrDefaultAsync(x => x.Id == id && (x.BrmModel == null || !x.BrmModel.IsDeleted));
        if (capability is null)
        {
            return NotFound();
        }

        var model = new CapabilityEditViewModel
        {
            Id = capability.Id,
            SelectedBrmModelId = capability.BrmModelId,
            SelectedBrmComponentId = capability.Mappings
                .Select(x => (int?)x.BrmComponentId)
                .Distinct()
                .FirstOrDefault(),
            Description = capability.Description,
            Notes = capability.Notes,
            MappingRows = capability.Mappings
                .OrderBy(x => x.Id)
                .Select(x => new CapabilityMappingRowInputViewModel
                {
                    ArmComponentId = x.ArmComponentId,
                    ArmCapabilityId = x.ArmCapabilityId
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
    public async Task<IActionResult> EditAsync(int id, CapabilityEditViewModel input)
    {
        var capability = await dbContext.BusinessCapabilityCatalogItems
            .Include(x => x.Mappings)
            .FirstOrDefaultAsync(x => x.Id == id && (x.BrmModel == null || !x.BrmModel.IsDeleted));
        if (capability is null)
        {
            return NotFound();
        }

        input.Id = id;
        input.SelectedBrmModelId = capability.BrmModelId;
        ModelState.Remove(nameof(input.SelectedBrmModelId));
        NormalizeInput(input);
        var normalizedMappings = await ValidateMappingsAsync(input);
        if (!ModelState.IsValid || normalizedMappings is null)
        {
            EnsureMappingRows(input.MappingRows);
            await PopulateOptionsAsync(input);
            return View(input);
        }

        var brmComponent = await dbContext.BrmComponents
            .AsNoTracking()
            .FirstAsync(x => x.Id == input.SelectedBrmComponentId!.Value);

        capability.Name = BuildBrmComponentLabel(brmComponent);
        capability.Description = NormalizeSelection(input.Description);
        capability.Notes = NormalizeSelection(input.Notes);
        capability.UpdatedUtc = DateTime.UtcNow;

        dbContext.BusinessCapabilityCatalogItemMappings.RemoveRange(capability.Mappings);
        capability.Mappings.Clear();

        foreach (var mapping in normalizedMappings)
        {
            capability.Mappings.Add(new BusinessCapabilityCatalogItemMapping
            {
                BrmComponentId = input.SelectedBrmComponentId!.Value,
                ArmComponentId = mapping.ArmComponentId,
                ArmCapabilityId = mapping.ArmCapabilityId,
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

        TempData["BrmModelsStatusMessage"] = $"Updated capability {capability.Name} in {await ResolveBrmModelNameAsync(capability.BrmModelId)}.";
        return RedirectToAction("Details", "BrmModels", new { id = capability.BrmModelId });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var model = await dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Where(x => x.Id == id && (x.BrmModel == null || !x.BrmModel.IsDeleted))
            .Select(x => new CapabilityDeleteViewModel
            {
                Id = x.Id,
                BrmModelId = x.BrmModelId,
                BrmModelName = x.BrmModel != null ? x.BrmModel.Name : "-",
                Name = x.Name,
                Description = x.Description,
                ArmComponentCount = x.Mappings
                    .Select(mapping => mapping.ArmComponentId)
                    .Distinct()
                    .Count(),
                UpdatedUtc = x.UpdatedUtc
            })
            .FirstOrDefaultAsync();
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmedAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var capability = await dbContext.BusinessCapabilityCatalogItems
            .Include(x => x.BrmModel)
            .FirstOrDefaultAsync(x => x.Id == id && (x.BrmModel == null || !x.BrmModel.IsDeleted));
        if (capability is null)
        {
            return NotFound();
        }

        var brmModelId = capability.BrmModelId;
        var brmModelName = capability.BrmModel?.Name ?? await ResolveBrmModelNameAsync(brmModelId);
        var capabilityName = capability.Name;

        dbContext.BusinessCapabilityCatalogItems.Remove(capability);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Capability",
            "Delete",
            nameof(BusinessCapabilityCatalogItem),
            id,
            $"Removed capability {capabilityName}.",
            $"Removed from BRM model {brmModelName}.");

        TempData["BrmModelsStatusMessage"] = $"Removed capability {capabilityName} from {brmModelName}.";

        return brmModelId is > 0
            ? RedirectToAction("Details", "BrmModels", new { id = brmModelId.Value })
            : RedirectToAction("Index", "BrmModels");
    }

    private async Task PopulateOptionsAsync(CapabilityEditViewModel model)
    {
        if (model.SelectedBrmModelId is > 0)
        {
            var brmModel = await dbContext.BrmModels
                .AsNoTracking()
                .Where(x => x.Id == model.SelectedBrmModelId.Value && !x.IsDeleted)
                .Select(x => new
                {
                    x.Name,
                    x.Area,
                    x.Status
                })
                .FirstOrDefaultAsync();

            if (brmModel is not null)
            {
                model.BrmModelName = brmModel.Name;
                model.BrmModelArea = brmModel.Area;
                model.BrmModelStatus = brmModel.Status;
            }
        }

        model.BrmComponentOptions = await dbContext.BrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem(
                $"{x.Code} {x.Name} ({x.ParentCapability!.ParentDomain!.Code}/{x.ParentCapability.Code})",
                x.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync();

        var armComponents = await dbContext.ArmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.ArmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .OrderBy(x => x.Code)
            .ToListAsync();

        model.ArmComponentOptions = armComponents
            .Select(x => new SelectListItem(
                $"{x.Code} {x.Name}",
                x.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ArmCapabilityOptions = armComponents
            .SelectMany(BuildArmCapabilityConnections)
            .GroupBy(x => x.ArmCapabilityId)
            .OrderBy(group => group.First().ArmCapabilityLabel, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SelectListItem(
                group.First().ConnectionLabel,
                group.Key.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        model.ArmComponentLookupOptions = armComponents
            .Select(x => new CapabilityArmComponentOptionViewModel
            {
                ArmComponentId = x.Id,
                ArmComponentLabel = $"{x.Code} {x.Name}",
                CapabilityOptions = BuildArmCapabilityConnections(x)
            })
            .ToList();
    }

    private async Task<List<NormalizedCapabilityMappingRow>?> ValidateMappingsAsync(CapabilityEditViewModel input)
    {
        var normalizedRows = new List<NormalizedCapabilityMappingRow>();

        if (!input.SelectedBrmModelId.HasValue)
        {
            ModelState.AddModelError(nameof(input.SelectedBrmModelId), "Choose a BRM model.");
        }
        else
        {
            var brmModelExists = await dbContext.BrmModels
                .AsNoTracking()
                .AnyAsync(x => x.Id == input.SelectedBrmModelId.Value && !x.IsDeleted);

            if (!brmModelExists)
            {
                ModelState.AddModelError(nameof(input.SelectedBrmModelId), "The selected BRM model could not be found.");
            }
        }

        if (!input.SelectedBrmComponentId.HasValue)
        {
            ModelState.AddModelError(nameof(input.SelectedBrmComponentId), "Choose a BRM capability.");
        }

        for (var index = 0; index < input.MappingRows.Count; index++)
        {
            var row = input.MappingRows[index];
            if (!row.ArmComponentId.HasValue)
            {
                continue;
            }

            var armComponent = await dbContext.ArmComponents
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id == row.ArmComponentId.Value)
                .Include(x => x.CapabilityLinks)
                .ThenInclude(x => x.ArmCapability)
                .ThenInclude(x => x!.ParentDomain)
                .Include(x => x.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .FirstOrDefaultAsync();

            if (armComponent is null)
            {
                ModelState.AddModelError($"MappingRows[{index}].ArmComponentId", $"ARM component {row.ArmComponentId.Value} could not be found.");
                continue;
            }

            var capabilityOptions = BuildArmCapabilityConnections(armComponent);
            if (capabilityOptions.Count == 0)
            {
                ModelState.AddModelError($"MappingRows[{index}].ArmComponentId", $"ARM component {armComponent.Code} {armComponent.Name} does not have any ARM capability connections.");
                continue;
            }

            var selectedArmCapabilityId = row.ArmCapabilityId;
            if (!selectedArmCapabilityId.HasValue)
            {
                if (capabilityOptions.Count == 1)
                {
                    selectedArmCapabilityId = capabilityOptions[0].ArmCapabilityId;
                    row.ArmCapabilityId = selectedArmCapabilityId;
                }
                else
                {
                    ModelState.AddModelError($"MappingRows[{index}].ArmCapabilityId", "Choose the ARM capability connection for this ARM component.");
                    continue;
                }
            }

            if (!capabilityOptions.Any(x => x.ArmCapabilityId == selectedArmCapabilityId.Value))
            {
                ModelState.AddModelError($"MappingRows[{index}].ArmCapabilityId", "The selected ARM capability is not linked to the chosen ARM component.");
                continue;
            }

            normalizedRows.Add(new NormalizedCapabilityMappingRow(row.ArmComponentId.Value, selectedArmCapabilityId.Value));
        }

        if (normalizedRows.Count == 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Add at least one supporting ARM component.");
            return null;
        }

        if (input.SelectedBrmComponentId.HasValue)
        {
            var brmExists = await dbContext.BrmComponents
                .AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.Id == input.SelectedBrmComponentId.Value);

            if (!brmExists)
            {
                ModelState.AddModelError(nameof(input.SelectedBrmComponentId), "The selected BRM capability could not be found.");
            }
        }

        var duplicateMappings = normalizedRows
            .GroupBy(x => new { x.ArmComponentId, x.ArmCapabilityId })
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateMappings.Count != 0)
        {
            ModelState.AddModelError(nameof(input.MappingRows), "Duplicate ARM component and capability connections are not allowed.");
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

    private static string BuildBrmComponentLabel(BrmComponent component) => $"{component.Code} {component.Name}";

    private static IReadOnlyList<CapabilityArmCapabilityOptionViewModel> BuildArmCapabilityConnections(ArmComponent component)
    {
        var capabilityOptions = component.CapabilityLinks
            .Where(x => x.ArmCapability?.ParentDomain is not null)
            .Select(x => x.ArmCapability!)
            .Append(component.ParentCapability)
            .Where(x => x?.ParentDomain is not null)
            .Select(x => new CapabilityArmCapabilityOptionViewModel
            {
                ArmCapabilityId = x!.Id,
                ArmDomainLabel = $"{x.ParentDomain!.Code} {x.ParentDomain.Name}",
                ArmCapabilityLabel = $"{x.Code} {x.Name}",
                ConnectionLabel = $"{x.Code} {x.Name} ({x.ParentDomain!.Code} {x.ParentDomain.Name})"
            })
            .GroupBy(x => x.ArmCapabilityId)
            .Select(group => group.First())
            .OrderBy(x => x.ArmCapabilityLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return capabilityOptions;
    }

    private async Task<string> ResolveBrmModelNameAsync(int? brmModelId)
    {
        if (!brmModelId.HasValue)
        {
            return "the selected BRM model";
        }

        return await dbContext.BrmModels
            .AsNoTracking()
            .Where(x => x.Id == brmModelId.Value && !x.IsDeleted)
            .Select(x => x.Name)
            .FirstOrDefaultAsync()
            ?? "the selected BRM model";
    }

    private sealed record NormalizedCapabilityMappingRow(int ArmComponentId, int ArmCapabilityId);
}
