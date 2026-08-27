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
    AuditLogService auditLogService,
    ReferenceModelDiagramService referenceModelDiagramService) : Controller
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

    public async Task<IActionResult> StructureAsync(int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var drmModel = await dbContext.DrmModels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (drmModel is null)
        {
            return NotFound();
        }

        var dataEntities = await dbContext.DrmModelDataEntities
            .AsNoTracking()
            .Where(x => x.DrmModelId == id)
            .Select(x => new
            {
                x.DrmEntityId,
                x.DrmCommonSubClassId
            })
            .ToListAsync(cancellationToken);

        return View(new DrmModelStructureViewModel
        {
            Id = drmModel.Id,
            Name = drmModel.Name,
            Area = drmModel.Area,
            Description = drmModel.Description,
            Status = drmModel.Status,
            UpdatedUtc = drmModel.UpdatedUtc,
            DataEntityCount = dataEntities.Count,
            EntityCount = dataEntities
                .Select(x => x.DrmEntityId)
                .Distinct()
                .Count(),
            CommonSubClassCount = dataEntities
                .Where(x => x.DrmCommonSubClassId.HasValue)
                .Select(x => x.DrmCommonSubClassId!.Value)
                .Distinct()
                .Count(),
            Diagram = await referenceModelDiagramService.BuildDrmModelAsync(
                drmModel.Id,
                onlySelectedNodes: true,
                cancellationToken)
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
            }).ToList(),
            HierarchyRoot = BuildDrmModelHierarchy(drmModel, dataEntities)
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

    private static ApplicationHierarchyNodeViewModel BuildDrmModelHierarchy(
        DrmModel drmModel,
        List<DrmModelDataEntity> dataEntities)
    {
        var rows = dataEntities
            .Select(x => new DrmModelHierarchyRow(
                x.DrmEntity?.ParentTopic?.TopicType?.Code ?? "unassigned-topic-type",
                x.DrmEntity?.ParentTopic?.TopicType?.DisplayLabel ?? "Unassigned topic type",
                x.DrmEntity?.ParentTopic?.Code ?? "unassigned-topic",
                x.DrmEntity?.ParentTopic?.DisplayLabel ?? "Unassigned topic",
                x.DrmEntity?.Code ?? $"entity-{x.DrmEntityId}",
                x.DrmEntity?.DisplayLabel ?? x.Name,
                x.DrmCommonSubClass?.Code,
                x.DrmCommonSubClass?.DisplayLabel))
            .ToList();

        var topicTypeNodes = rows
            .GroupBy(row => new { row.TopicTypeKey, row.TopicTypeLabel })
            .OrderBy(group => group.Key.TopicTypeKey, StringComparer.OrdinalIgnoreCase)
            .Select(topicTypeGroup => new ApplicationHierarchyNodeViewModel
            {
                Key = $"drm-model-{drmModel.Id}-topic-type-{NormalizeHierarchyKey(topicTypeGroup.Key.TopicTypeKey)}",
                NodeType = "Topic type",
                CssType = "drm-domain",
                Label = topicTypeGroup.Key.TopicTypeLabel,
                PathCount = topicTypeGroup.Count(),
                IsExpanded = true,
                Children = topicTypeGroup
                    .GroupBy(row => new { row.TopicKey, row.TopicLabel })
                    .OrderBy(group => group.Key.TopicKey, StringComparer.OrdinalIgnoreCase)
                    .Select(topicGroup => new ApplicationHierarchyNodeViewModel
                    {
                        Key = $"drm-model-{drmModel.Id}-topic-{NormalizeHierarchyKey(topicGroup.Key.TopicKey)}",
                        NodeType = "Topic",
                        CssType = "drm-capability",
                        Label = topicGroup.Key.TopicLabel,
                        PathCount = topicGroup.Count(),
                        IsExpanded = true,
                        Children = topicGroup
                            .GroupBy(row => new { row.EntityKey, row.EntityLabel })
                            .OrderBy(group => group.Key.EntityKey, StringComparer.OrdinalIgnoreCase)
                            .Select(entityGroup => new ApplicationHierarchyNodeViewModel
                            {
                                Key = $"drm-model-{drmModel.Id}-entity-{NormalizeHierarchyKey(entityGroup.Key.EntityKey)}",
                                NodeType = "Data entity",
                                CssType = "drm-component",
                                Label = entityGroup.Key.EntityLabel,
                                PathCount = entityGroup.Count(),
                                IsExpanded = true,
                                Children = entityGroup
                                    .Where(row => !string.IsNullOrWhiteSpace(row.SubClassKey))
                                    .GroupBy(row => new { row.SubClassKey, row.SubClassLabel })
                                    .OrderBy(group => group.Key.SubClassKey, StringComparer.OrdinalIgnoreCase)
                                    .Select(subClassGroup => new ApplicationHierarchyNodeViewModel
                                    {
                                        Key = $"drm-model-{drmModel.Id}-subclass-{NormalizeHierarchyKey(subClassGroup.Key.SubClassKey!)}",
                                        NodeType = "Common sub-class",
                                        CssType = "drm-subclass",
                                        Label = subClassGroup.Key.SubClassLabel ?? subClassGroup.Key.SubClassKey!,
                                        PathCount = subClassGroup.Count(),
                                        IsExpanded = true
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return new ApplicationHierarchyNodeViewModel
        {
            Key = $"drm-model-{drmModel.Id}",
            NodeType = "DRM model",
            CssType = "drm-model",
            Label = drmModel.Name,
            PathCount = dataEntities.Count,
            IsExpanded = true,
            Children = topicTypeNodes
        };
    }

    private static string NormalizeHierarchyKey(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant().Replace(' ', '-');

    private sealed record DrmModelHierarchyRow(
        string TopicTypeKey,
        string TopicTypeLabel,
        string TopicKey,
        string TopicLabel,
        string EntityKey,
        string EntityLabel,
        string? SubClassKey,
        string? SubClassLabel);
}
