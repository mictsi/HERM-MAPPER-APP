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
public sealed class DrmDataEntitiesController(
    AppDbContext dbContext,
    AuditLogService auditLogService) : Controller
{
    public Task<IActionResult> IndexAsync(int? drmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return Task.FromResult<IActionResult>(BadRequest(ModelState));
        }

        IActionResult result = drmModelId is > 0
            ? RedirectToAction("Details", "DrmModels", new { id = drmModelId.Value })
            : RedirectToAction("Index", "DrmModels");

        return Task.FromResult(result);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> CreateAsync(int? drmModelId = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (drmModelId is not > 0)
        {
            return RedirectToAction("Index", "DrmModels");
        }

        var drmModelExists = await dbContext.DrmModels
            .AsNoTracking()
            .AnyAsync(x => x.Id == drmModelId.Value && !x.IsDeleted);
        if (!drmModelExists)
        {
            return RedirectToAction("Index", "DrmModels");
        }

        var model = new DrmDataEntityEditViewModel
        {
            SelectedDrmModelId = drmModelId
        };
        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsync(int drmModelId, DrmDataEntityEditViewModel input)
    {
        if (drmModelId <= 0)
        {
            return RedirectToAction("Index", "DrmModels");
        }

        input.SelectedDrmModelId = drmModelId;
        ModelState.Remove(nameof(input.SelectedDrmModelId));
        NormalizeInput(input);
        var selection = await ValidateSelectionAsync(input);
        if (!ModelState.IsValid || selection is null)
        {
            await PopulateOptionsAsync(input);
            return View(input);
        }

        var item = new DrmModelDataEntity
        {
            DrmModelId = drmModelId,
            DrmEntityId = selection.Entity.Id,
            DrmCommonSubClassId = selection.SubClass?.Id,
            Name = selection.SubClass?.DisplayLabel ?? selection.Entity.DisplayLabel,
            Description = NormalizeOptionalText(input.Description) ?? selection.SubClass?.Description ?? selection.Entity.Description,
            Notes = NormalizeOptionalText(input.Notes),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        dbContext.DrmModelDataEntities.Add(item);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmDataEntity",
            "Create",
            nameof(DrmModelDataEntity),
            item.Id,
            $"Added DRM data entity {item.Name}.",
            $"Added to DRM model {await ResolveDrmModelNameAsync(drmModelId)}.");

        TempData["DrmModelsStatusMessage"] = $"Added DRM data entity {item.Name}.";
        return RedirectToAction("Details", "DrmModels", new { id = drmModelId });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> EditAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var item = await dbContext.DrmModelDataEntities
            .AsNoTracking()
            .Include(x => x.DrmModel)
            .FirstOrDefaultAsync(x => x.Id == id && x.DrmModel != null && !x.DrmModel.IsDeleted);
        if (item is null)
        {
            return NotFound();
        }

        var model = new DrmDataEntityEditViewModel
        {
            Id = item.Id,
            SelectedDrmModelId = item.DrmModelId,
            SelectedDrmEntityId = item.DrmEntityId,
            SelectedDrmCommonSubClassId = item.DrmCommonSubClassId,
            Description = item.Description,
            Notes = item.Notes
        };

        await PopulateOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAsync(int id, DrmDataEntityEditViewModel input)
    {
        var item = await dbContext.DrmModelDataEntities
            .Include(x => x.DrmModel)
            .FirstOrDefaultAsync(x => x.Id == id && x.DrmModel != null && !x.DrmModel.IsDeleted);
        if (item is null)
        {
            return NotFound();
        }

        input.Id = id;
        input.SelectedDrmModelId = item.DrmModelId;
        ModelState.Remove(nameof(input.SelectedDrmModelId));
        NormalizeInput(input);
        var selection = await ValidateSelectionAsync(input, id);
        if (!ModelState.IsValid || selection is null)
        {
            await PopulateOptionsAsync(input);
            return View(input);
        }

        item.DrmEntityId = selection.Entity.Id;
        item.DrmCommonSubClassId = selection.SubClass?.Id;
        item.Name = selection.SubClass?.DisplayLabel ?? selection.Entity.DisplayLabel;
        item.Description = NormalizeOptionalText(input.Description) ?? selection.SubClass?.Description ?? selection.Entity.Description;
        item.Notes = NormalizeOptionalText(input.Notes);
        item.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmDataEntity",
            "Update",
            nameof(DrmModelDataEntity),
            item.Id,
            $"Updated DRM data entity {item.Name}.",
            $"Updated in DRM model {await ResolveDrmModelNameAsync(item.DrmModelId)}.");

        TempData["DrmModelsStatusMessage"] = $"Updated DRM data entity {item.Name}.";
        return RedirectToAction("Details", "DrmModels", new { id = item.DrmModelId });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> DeleteAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var model = await dbContext.DrmModelDataEntities
            .AsNoTracking()
            .Where(x => x.Id == id && x.DrmModel != null && !x.DrmModel.IsDeleted)
            .Select(x => new DrmDataEntityDeleteViewModel
            {
                Id = x.Id,
                DrmModelId = x.DrmModelId,
                DrmModelName = x.DrmModel!.Name,
                Name = x.Name,
                Description = x.Description,
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

        var item = await dbContext.DrmModelDataEntities
            .Include(x => x.DrmModel)
            .FirstOrDefaultAsync(x => x.Id == id && x.DrmModel != null && !x.DrmModel.IsDeleted);
        if (item is null)
        {
            return NotFound();
        }

        var drmModelId = item.DrmModelId;
        var itemName = item.Name;

        dbContext.DrmModelDataEntities.Remove(item);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "DrmDataEntity",
            "Delete",
            nameof(DrmModelDataEntity),
            id,
            $"Removed DRM data entity {itemName}.",
            $"Removed from DRM model {item.DrmModel?.Name ?? "-"}.");

        TempData["DrmModelsStatusMessage"] = $"Removed DRM data entity {itemName}.";
        return RedirectToAction("Details", "DrmModels", new { id = drmModelId });
    }

    private async Task PopulateOptionsAsync(DrmDataEntityEditViewModel model)
    {
        if (model.SelectedDrmModelId is > 0)
        {
            var drmModel = await dbContext.DrmModels
                .AsNoTracking()
                .Where(x => x.Id == model.SelectedDrmModelId.Value && !x.IsDeleted)
                .Select(x => new
                {
                    x.Name,
                    x.Area,
                    x.Status
                })
                .FirstOrDefaultAsync();

            if (drmModel is not null)
            {
                model.DrmModelName = drmModel.Name;
                model.DrmModelArea = drmModel.Area;
                model.DrmModelStatus = drmModel.Status;
            }
        }

        model.EntityOptions = await dbContext.DrmEntities
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ParentTopic)
            .ThenInclude(x => x!.TopicType)
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem(
                $"{x.Code} {x.Name} (Topic Type = {x.ParentTopic!.TopicType!.Name} --> Topic = {x.ParentTopic.Code} {x.ParentTopic.Name})",
                x.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync();

        var entityLookups = await dbContext.DrmEntities
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name
            })
            .ToListAsync();

        var entitiesById = entityLookups.ToDictionary(x => x.Id);
        var entitiesByCode = entityLookups
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var subClassLookups = await dbContext.DrmCommonSubClasses
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.ParentEntityId,
                x.ParentEntityCode
            })
            .ToListAsync();

        model.CommonSubClassOptions = subClassLookups
            .Select(subClass =>
            {
                // Catalogues imported before the parent key existed only carry the parent code,
                // so fall back to the code when the identifier is missing.
                var parent = subClass.ParentEntityId.HasValue && entitiesById.TryGetValue(subClass.ParentEntityId.Value, out var byId)
                    ? byId
                    : !string.IsNullOrWhiteSpace(subClass.ParentEntityCode) && entitiesByCode.TryGetValue(subClass.ParentEntityCode, out var byCode)
                        ? byCode
                        : null;

                return parent is null
                    ? null
                    : new DrmCommonSubClassOptionViewModel
                    {
                        Id = subClass.Id,
                        ParentEntityId = parent.Id,
                        Label = $"{subClass.Code} {subClass.Name} ({parent.Code} {parent.Name})"
                    };
            })
            .Where(option => option is not null)
            .Select(option => option!)
            .ToList();
    }

    private async Task<DrmSelection?> ValidateSelectionAsync(DrmDataEntityEditViewModel input, int? existingItemId = null)
    {
        if (!input.SelectedDrmModelId.HasValue)
        {
            ModelState.AddModelError(nameof(input.SelectedDrmModelId), "Choose a DRM model.");
        }
        else
        {
            var drmModelExists = await dbContext.DrmModels
                .AsNoTracking()
                .AnyAsync(x => x.Id == input.SelectedDrmModelId.Value && !x.IsDeleted);
            if (!drmModelExists)
            {
                ModelState.AddModelError(nameof(input.SelectedDrmModelId), "The selected DRM model could not be found.");
            }
        }

        DrmEntity? entity = null;
        if (!input.SelectedDrmEntityId.HasValue)
        {
            ModelState.AddModelError(nameof(input.SelectedDrmEntityId), "Choose a DRM entity.");
        }
        else
        {
            entity = await dbContext.DrmEntities
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == input.SelectedDrmEntityId.Value && !x.IsDeleted);
            if (entity is null)
            {
                ModelState.AddModelError(nameof(input.SelectedDrmEntityId), "The selected DRM entity could not be found.");
            }
        }

        DrmCommonSubClass? subClass = null;
        if (input.SelectedDrmCommonSubClassId.HasValue)
        {
            subClass = await dbContext.DrmCommonSubClasses
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == input.SelectedDrmCommonSubClassId.Value && !x.IsDeleted);
            if (subClass is null)
            {
                ModelState.AddModelError(nameof(input.SelectedDrmCommonSubClassId), "The selected common sub-class could not be found.");
            }
            else if (entity is not null && !BelongsToEntity(subClass, entity))
            {
                ModelState.AddModelError(nameof(input.SelectedDrmCommonSubClassId), "The selected common sub-class does not belong to the selected entity.");
            }
        }

        if (entity is not null && input.SelectedDrmModelId.HasValue)
        {
            var duplicateExists = await dbContext.DrmModelDataEntities
                .AsNoTracking()
                .AnyAsync(x =>
                    x.DrmModelId == input.SelectedDrmModelId.Value &&
                    x.DrmEntityId == entity.Id &&
                    x.DrmCommonSubClassId == input.SelectedDrmCommonSubClassId &&
                    (!existingItemId.HasValue || x.Id != existingItemId.Value));

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(input.SelectedDrmEntityId), "This DRM entity selection already exists in the model.");
            }
        }

        return ModelState.IsValid && entity is not null
            ? new DrmSelection(entity, subClass)
            : null;
    }

    private static bool BelongsToEntity(DrmCommonSubClass subClass, DrmEntity entity) =>
        subClass.ParentEntityId.HasValue
            ? subClass.ParentEntityId.Value == entity.Id
            : !string.IsNullOrWhiteSpace(subClass.ParentEntityCode) &&
              string.Equals(subClass.ParentEntityCode, entity.Code, StringComparison.OrdinalIgnoreCase);

    private static void NormalizeInput(DrmDataEntityEditViewModel input)
    {
        input.Description = NormalizeOptionalText(input.Description);
        input.Notes = NormalizeOptionalText(input.Notes);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task<string> ResolveDrmModelNameAsync(int drmModelId) =>
        await dbContext.DrmModels
            .AsNoTracking()
            .Where(x => x.Id == drmModelId && !x.IsDeleted)
            .Select(x => x.Name)
            .FirstOrDefaultAsync()
        ?? "the selected DRM model";

    private sealed record DrmSelection(DrmEntity Entity, DrmCommonSubClass? SubClass);
}
