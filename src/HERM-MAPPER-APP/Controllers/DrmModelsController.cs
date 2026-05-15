using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class DrmModelsController(
    AppDbContext dbContext,
    AuditLogService auditLogService) : Controller
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

    public async Task<IActionResult> IndexAsync()
    {
        return View(new DrmModelsIndexViewModel
        {
            StatusMessage = TempData["DrmModelsStatusMessage"] as string,
            Models = await dbContext.DrmModels
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Area)
                .Select(x => new DrmModelIndexRowViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Area = x.Area,
                    Description = x.Description,
                    Status = x.Status,
                    DataEntityCount = x.DataEntities.Count,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToListAsync()
        });
    }

    public async Task<IActionResult> DetailsAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var drmModel = await dbContext.DrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (drmModel is null)
        {
            return NotFound();
        }

        var dataEntities = await dbContext.DrmModelDataEntities
            .AsNoTracking()
            .Where(x => x.DrmModelId == id)
            .Include(x => x.DrmEntity)
            .ThenInclude(x => x!.ParentTopic)
            .ThenInclude(x => x!.TopicType)
            .Include(x => x.DrmCommonSubClass)
            .AsSplitQuery()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return View(new DrmModelDetailsViewModel
        {
            Id = drmModel.Id,
            Name = drmModel.Name,
            Area = drmModel.Area,
            Description = drmModel.Description,
            Status = drmModel.Status,
            UpdatedUtc = drmModel.UpdatedUtc,
            TopicTypeCount = dataEntities
                .Select(x => x.DrmEntity?.ParentTopic?.TopicTypeId)
                .Where(x => x.HasValue)
                .Distinct()
                .Count(),
            TopicCount = dataEntities
                .Select(x => x.DrmEntity?.ParentTopicId)
                .Where(x => x.HasValue)
                .Distinct()
                .Count(),
            EntityCount = dataEntities
                .Select(x => x.DrmEntityId)
                .Distinct()
                .Count(),
            CommonSubClassCount = dataEntities
                .Select(x => x.DrmCommonSubClassId)
                .Where(x => x.HasValue)
                .Distinct()
                .Count(),
            StatusMessage = TempData["DrmModelsStatusMessage"] as string,
            DataEntities = dataEntities.Select(x => new DrmModelDataEntityRowViewModel
            {
                Id = x.Id,
                Name = x.Name,
                TopicTypeLabel = x.DrmEntity?.ParentTopic?.TopicType?.DisplayLabel ?? "-",
                TopicLabel = x.DrmEntity?.ParentTopic?.DisplayLabel ?? "-",
                EntityLabel = x.DrmEntity?.DisplayLabel ?? "-",
                CommonSubClassLabel = x.DrmCommonSubClass?.DisplayLabel,
                Description = x.Description,
                UpdatedUtc = x.UpdatedUtc
            }).ToList()
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public IActionResult Create() => View(BuildEditViewModel());

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync(DrmModelEditViewModel input)
    {
        NormalizeInput(input);
        ValidateStatus(input);
        if (!ModelState.IsValid)
        {
            return View(BuildEditViewModel(input));
        }

        var drmModel = new DrmModel
        {
            Name = input.Name,
            Area = input.Area,
            Description = input.Description,
            Status = input.Status,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        dbContext.DrmModels.Add(drmModel);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmModel",
            "Create",
            nameof(DrmModel),
            drmModel.Id,
            $"Created DRM model {drmModel.Name}.",
            $"Area: {drmModel.Area}. Status: {drmModel.Status}.");

        TempData["DrmModelsStatusMessage"] = $"Created DRM model {drmModel.Name}.";
        return RedirectToAction("Details", new { id = drmModel.Id });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> EditAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var drmModel = await dbContext.DrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (drmModel is null)
        {
            return NotFound();
        }

        return View(BuildEditViewModel(new DrmModelEditViewModel
        {
            Id = drmModel.Id,
            Name = drmModel.Name,
            Area = drmModel.Area,
            Description = drmModel.Description,
            Status = drmModel.Status
        }));
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAsync(int id, DrmModelEditViewModel input)
    {
        var drmModel = await dbContext.DrmModels
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (drmModel is null)
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

        drmModel.Name = input.Name;
        drmModel.Area = input.Area;
        drmModel.Description = input.Description;
        drmModel.Status = input.Status;
        drmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmModel",
            "Update",
            nameof(DrmModel),
            drmModel.Id,
            $"Updated DRM model {drmModel.Name}.",
            $"Area: {drmModel.Area}. Status: {drmModel.Status}.");

        TempData["DrmModelsStatusMessage"] = $"Updated DRM model {drmModel.Name}.";
        return RedirectToAction("Details", new { id = drmModel.Id });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var drmModel = await dbContext.DrmModels
            .AsNoTracking()
            .Include(x => x.DataEntities)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

        return drmModel is null ? NotFound() : View(drmModel);
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

        var drmModel = await dbContext.DrmModels.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (drmModel is null)
        {
            return NotFound();
        }

        drmModel.IsDeleted = true;
        drmModel.DeletedUtc = DateTime.UtcNow;
        drmModel.DeletedReason = "Moved to trash from the DRM model catalogue.";
        drmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmModel",
            "Delete",
            nameof(DrmModel),
            id,
            $"Moved DRM model {drmModel.Name} to trash.",
            drmModel.DeletedReason);

        TempData["DrmModelsStatusMessage"] = $"Moved DRM model {drmModel.Name} to trash.";
        return RedirectToAction("Index");
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var models = await dbContext.DrmModels
            .AsNoTracking()
            .Include(x => x.DataEntities)
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedUtc)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Area)
            .ToListAsync();

        return View(new DrmModelRestoreViewModel
        {
            Models = models,
            StatusMessage = TempData["DrmModelsStatusMessage"] as string
        });
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreDeletedAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var drmModel = await dbContext.DrmModels.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (drmModel is null)
        {
            return NotFound();
        }

        drmModel.IsDeleted = false;
        drmModel.DeletedUtc = null;
        drmModel.DeletedReason = null;
        drmModel.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmModel",
            "Restore",
            nameof(DrmModel),
            drmModel.Id,
            $"Restored DRM model {drmModel.Name} from trash.");

        TempData["DrmModelsStatusMessage"] = $"Restored DRM model {drmModel.Name}.";
        return RedirectToAction("Restore");
    }

    private static void NormalizeInput(DrmModelEditViewModel input)
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

    private void ValidateStatus(DrmModelEditViewModel input)
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

    private static DrmModelEditViewModel BuildEditViewModel(DrmModelEditViewModel? source = null) =>
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
