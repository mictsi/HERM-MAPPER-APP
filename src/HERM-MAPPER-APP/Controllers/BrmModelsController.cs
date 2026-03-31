using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class BrmModelsController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    HermDrilldownService drilldownService) : Controller
{
    private static readonly IReadOnlyList<string> SuggestedStatuses =
    [
        "Draft",
        "Proposal",
        "In Review",
        "Pilot",
        "Production",
        "Retired"
    ];

    public async Task<IActionResult> Index()
    {
        return View(new BrmModelsIndexViewModel
        {
            StatusMessage = TempData["BrmModelsStatusMessage"] as string,
            Models = await dbContext.BrmModels
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Area)
                .Select(x => new BrmModelIndexRowViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Area = x.Area,
                    Description = x.Description,
                    Status = x.Status,
                    CapabilityCount = x.Capabilities.Count,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToListAsync()
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brmModel = await dbContext.BrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (brmModel is null)
        {
            return NotFound();
        }

        var capabilities = await dbContext.BusinessCapabilityCatalogItems
            .AsNoTracking()
            .Where(x => x.BrmModelId == id)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.BrmComponent)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmCapability)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.ArmComponent)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync();

        var applicationCountsByCapabilityId = capabilities.ToDictionary(capability => capability.Id, _ => 0);
        var productCountsByCapabilityId = capabilities.ToDictionary(capability => capability.Id, _ => 0);

        var armComponentIds = capabilities
            .SelectMany(x => x.Mappings)
            .Select(x => x.ArmComponentId)
            .Distinct()
            .ToList();

        if (armComponentIds.Count != 0)
        {
            var applicationMappings = await dbContext.ApplicationCatalogItemMappings
                .AsNoTracking()
                .Where(x =>
                    armComponentIds.Contains(x.ArmComponentId) &&
                    x.ApplicationCatalogItem != null &&
                    !x.ApplicationCatalogItem.IsDeleted)
                .Select(x => new { x.ApplicationCatalogItemId, x.ArmComponentId, x.ProductCatalogItemId })
                .ToListAsync();

            foreach (var capability in capabilities)
            {
                var mappedArmComponentIds = capability.Mappings
                    .Select(x => x.ArmComponentId)
                    .Distinct()
                    .ToHashSet();

                var matchingMappings = applicationMappings
                    .Where(x => mappedArmComponentIds.Contains(x.ArmComponentId))
                    .ToList();

                applicationCountsByCapabilityId[capability.Id] = matchingMappings
                    .Select(x => x.ApplicationCatalogItemId)
                    .Distinct()
                    .Count();

                productCountsByCapabilityId[capability.Id] = matchingMappings
                    .Select(x => x.ProductCatalogItemId)
                    .Distinct()
                    .Count();
            }
        }

        return View(new BrmModelDetailsViewModel
        {
            Id = brmModel.Id,
            Name = brmModel.Name,
            Area = brmModel.Area,
            Description = brmModel.Description,
            Status = brmModel.Status,
            UpdatedUtc = brmModel.UpdatedUtc,
            CapabilityCount = capabilities.Count,
            StatusMessage = TempData["BrmModelsStatusMessage"] as string,
            HierarchyRoot = await drilldownService.BuildBrmModelHierarchyAsync(id),
            Capabilities = capabilities
                .Select(capability => new BrmModelCapabilityRowViewModel
                {
                    Id = capability.Id,
                    Name = capability.Name,
                    Description = capability.Description,
                    ArmComponentCount = capability.Mappings
                        .Select(x => x.ArmComponentId)
                        .Distinct()
                        .Count(),
                    ApplicationCount = applicationCountsByCapabilityId.GetValueOrDefault(capability.Id),
                    ProductCount = productCountsByCapabilityId.GetValueOrDefault(capability.Id),
                    UpdatedUtc = capability.UpdatedUtc
                })
                .ToList()
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public IActionResult Create() => View(BuildEditViewModel());

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BrmModelEditViewModel input)
    {
        NormalizeInput(input);
        ValidateStatus(input);
        if (!ModelState.IsValid)
        {
            return View(BuildEditViewModel(input));
        }

        var brmModel = new BrmModel
        {
            Name = input.Name,
            Area = input.Area,
            Description = input.Description,
            Status = input.Status,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        dbContext.BrmModels.Add(brmModel);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "BrmModel",
            "Create",
            nameof(BrmModel),
            brmModel.Id,
            $"Created BRM model {brmModel.Name}.",
            $"Area: {brmModel.Area}. Status: {brmModel.Status}.");

        TempData["BrmModelsStatusMessage"] = $"Created BRM model {brmModel.Name}.";
        return RedirectToAction(nameof(Details), new { id = brmModel.Id });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Edit(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brmModel = await dbContext.BrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (brmModel is null)
        {
            return NotFound();
        }

        return View(BuildEditViewModel(new BrmModelEditViewModel
        {
            Id = brmModel.Id,
            Name = brmModel.Name,
            Area = brmModel.Area,
            Description = brmModel.Description,
            Status = brmModel.Status
        }));
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BrmModelEditViewModel input)
    {
        var brmModel = await dbContext.BrmModels
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (brmModel is null)
        {
            return NotFound();
        }

        input.Id = id;
        NormalizeInput(input);
        ValidateStatus(input);
        if (!ModelState.IsValid)
        {
            return View(BuildEditViewModel(input));
        }

        brmModel.Name = input.Name;
        brmModel.Area = input.Area;
        brmModel.Description = input.Description;
        brmModel.Status = input.Status;
        brmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "BrmModel",
            "Update",
            nameof(BrmModel),
            brmModel.Id,
            $"Updated BRM model {brmModel.Name}.",
            $"Area: {brmModel.Area}. Status: {brmModel.Status}.");

        TempData["BrmModelsStatusMessage"] = $"Updated BRM model {brmModel.Name}.";
        return RedirectToAction(nameof(Details), new { id = brmModel.Id });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brmModel = await dbContext.BrmModels
            .AsNoTracking()
            .Include(x => x.Capabilities)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

        return brmModel is null ? NotFound() : View(brmModel);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brmModel = await dbContext.BrmModels.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (brmModel is null)
        {
            return NotFound();
        }

        brmModel.IsDeleted = true;
        brmModel.DeletedUtc = DateTime.UtcNow;
        brmModel.DeletedReason = "Moved to trash from the BRM model catalogue.";
        brmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "BrmModel",
            "Delete",
            nameof(BrmModel),
            id,
            $"Moved BRM model {brmModel.Name} to trash.",
            brmModel.DeletedReason);

        TempData["BrmModelsStatusMessage"] = $"Moved BRM model {brmModel.Name} to trash.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> Restore()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var models = await dbContext.BrmModels
            .AsNoTracking()
            .Include(x => x.Capabilities)
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedUtc)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Area)
            .ToListAsync();

        return View(new BrmModelRestoreViewModel
        {
            Models = models,
            StatusMessage = TempData["BrmModelsStatusMessage"] as string
        });
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreDeleted(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var brmModel = await dbContext.BrmModels.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (brmModel is null)
        {
            return NotFound();
        }

        brmModel.IsDeleted = false;
        brmModel.DeletedUtc = null;
        brmModel.DeletedReason = null;
        brmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "BrmModel",
            "Restore",
            nameof(BrmModel),
            brmModel.Id,
            $"Restored BRM model {brmModel.Name} from trash.");

        TempData["BrmModelsStatusMessage"] = $"Restored BRM model {brmModel.Name}.";
        return RedirectToAction(nameof(Restore));
    }

    private static void NormalizeInput(BrmModelEditViewModel input)
    {
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Area = input.Area?.Trim() ?? string.Empty;
        input.Description = NormalizeOptionalText(input.Description);
        input.Status = NormalizeStatus(input.Status);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var matchingStatus = SuggestedStatuses.FirstOrDefault(status =>
            string.Equals(status, normalized, StringComparison.OrdinalIgnoreCase));

        return matchingStatus ?? normalized;
    }

    private void ValidateStatus(BrmModelEditViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Status))
        {
            return;
        }

        if (SuggestedStatuses.Contains(input.Status, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ModelState.AddModelError(nameof(input.Status), "Choose a status from the dropdown list.");
    }

    private static BrmModelEditViewModel BuildEditViewModel(BrmModelEditViewModel? source = null) =>
        new()
        {
            Id = source?.Id,
            Name = source?.Name ?? string.Empty,
            Area = source?.Area ?? string.Empty,
            Description = source?.Description,
            Status = source?.Status ?? SuggestedStatuses[0],
            SuggestedStatuses = SuggestedStatuses
        };
}
