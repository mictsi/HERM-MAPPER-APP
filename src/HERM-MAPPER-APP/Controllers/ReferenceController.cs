using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class ReferenceController(
    AppDbContext dbContext,
    TrmWorkbookImportService workbookImportService,
    ComponentVersioningService componentVersioningService,
    AuditLogService auditLogService,
    IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> IndexAsync(
        string? search,
        int? domainId,
        int? capabilityId,
        ReferenceModelKind? modelKind = null,
        string? domainCode = null,
        string? capabilityCode = null,
        string? componentCode = null,
        string? subClassCode = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View(await BuildViewModelAsync(
            search,
            domainId,
            capabilityId,
            modelKind,
            domainCode,
            capabilityCode,
            componentCode,
            subClassCode,
            null,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreAsync()
    {
        return View("Restore", await BuildRestoreViewModelAsync(
            ReferenceModelKind.Trm,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreArmAsync()
    {
        return View("Restore", await BuildRestoreViewModelAsync(
            ReferenceModelKind.Arm,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreBrmAsync()
    {
        return View("Restore", await BuildRestoreViewModelAsync(
            ReferenceModelKind.Brm,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreDrmAsync()
    {
        return View("Restore", await BuildRestoreViewModelAsync(
            ReferenceModelKind.Drm,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyImportAsync(IFormFile? workbook)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (workbook is null || workbook.Length == 0)
        {
            return View("Index", await BuildViewModelAsync(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new WorkbookImportReviewViewModel
                {
                    Verification = new TrmWorkbookVerificationResult
                    {
                        Errors = ["Choose an .xlsx workbook before verifying the import."]
                    }
                },
                null));
        }

        if (!string.Equals(Path.GetExtension(workbook.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return View("Index", await BuildViewModelAsync(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new WorkbookImportReviewViewModel
                {
                    UploadedFileName = workbook.FileName,
                    Verification = new TrmWorkbookVerificationResult
                    {
                        Errors = ["Only Excel .xlsx workbooks are supported."]
                    }
                },
                null));
        }

        var pendingImportToken = Guid.NewGuid().ToString("N");
        var pendingDirectory = EnsurePendingImportDirectory();
        var pendingPath = Path.Combine(pendingDirectory, $"{pendingImportToken}.xlsx");

        await using (var stream = System.IO.File.Create(pendingPath))
        {
            await workbook.CopyToAsync(stream);
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath, ReferenceModelKind.Trm);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
        }

        await auditLogService.WriteAsync(
            "Reference",
            "VerifyImport",
            "TrmWorkbook",
            null,
            $"Verified workbook {workbook.FileName}.",
            verification.IsValid ? "Verification passed." : string.Join(" | ", verification.Errors));

        return View("Index", await BuildViewModelAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new WorkbookImportReviewViewModel
            {
                PendingImportToken = verification.IsValid ? pendingImportToken : null,
                UploadedFileName = workbook.FileName,
                Verification = verification
            },
            null));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportVerifiedAsync(string pendingImportToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ImportStatusMessage"] = "Verify a workbook before importing it.";
            return RedirectToAction("Index");
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory(), $"{pendingImportToken}.xlsx");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ImportStatusMessage"] = "The verified workbook is no longer available. Upload it again.";
            return RedirectToAction("Index");
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath, ReferenceModelKind.Trm);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View("Index", await BuildViewModelAsync(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new WorkbookImportReviewViewModel
                {
                    Verification = verification
                },
                null));
        }

        var summary = await workbookImportService.ImportAsync(pendingPath, ReferenceModelKind.Trm);
        System.IO.File.Delete(pendingPath);

        TempData["ImportStatusMessage"] =
            $"TRM model imported. Domains +{summary.DomainsAdded}/{summary.DomainsUpdated} updated, " +
            $"capabilities +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated} updated, " +
            $"components +{summary.ComponentsAdded}/{summary.ComponentsUpdated} updated.";

        return RedirectToAction("Index");
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComponentAsync(int id, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        switch (modelKind)
        {
            case ReferenceModelKind.Trm:
            {
                var component = await dbContext.TrmComponents
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = true;
                component.DeletedUtc = DateTime.UtcNow;
                component.DeletedReason = "Moved to trash from the TRM model catalogue.";

                await dbContext.SaveChangesAsync();
                await componentVersioningService.RecordVersionAsync(component.Id, "Deleted", component.DeletedReason);
                await auditLogService.WriteAsync(
                    "Component",
                    "Delete",
                    nameof(TrmComponent),
                    component.Id,
                    $"Moved TRM component {component.DisplayLabel} to trash.",
                    component.DeletedReason);

                TempData["ImportStatusMessage"] = $"Moved TRM component {component.DisplayLabel} to trash.";
                break;
            }
            case ReferenceModelKind.Arm:
            {
                var component = await dbContext.ArmComponents
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = true;
                component.DeletedUtc = DateTime.UtcNow;
                component.DeletedReason = "Moved to trash from the ARM model catalogue.";

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Delete",
                    nameof(ArmComponent),
                    component.Id,
                    $"Moved ARM component {component.DisplayLabel} to trash.",
                    component.DeletedReason);

                TempData["ImportStatusMessage"] = $"Moved ARM component {component.DisplayLabel} to trash.";
                break;
            }
            case ReferenceModelKind.Brm:
            {
                var component = await dbContext.BrmComponents
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = true;
                component.DeletedUtc = DateTime.UtcNow;
                component.DeletedReason = "Moved to trash from the BRM model catalogue.";

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Delete",
                    nameof(BrmComponent),
                    component.Id,
                    $"Moved BRM component {component.DisplayLabel} to trash.",
                    component.DeletedReason);

                TempData["ImportStatusMessage"] = $"Moved BRM component {component.DisplayLabel} to trash.";
                break;
            }
            case ReferenceModelKind.Drm:
            {
                if (id < 0)
                {
                    var subClassId = Math.Abs(id);
                    var subClass = await dbContext.DrmCommonSubClasses
                        .FirstOrDefaultAsync(x => x.Id == subClassId && !x.IsDeleted);
                    if (subClass is null)
                    {
                        return NotFound();
                    }

                    subClass.IsDeleted = true;
                    subClass.DeletedUtc = DateTime.UtcNow;
                    subClass.DeletedReason = "Moved to trash from the DRM model catalogue.";

                    await dbContext.SaveChangesAsync();
                    await auditLogService.WriteAsync(
                        "Component",
                        "Delete",
                        nameof(DrmCommonSubClass),
                        subClass.Id,
                        $"Moved DRM common sub-class {subClass.DisplayLabel} to trash.",
                        subClass.DeletedReason);

                    TempData["ImportStatusMessage"] = $"Moved DRM common sub-class {subClass.DisplayLabel} to trash.";
                    break;
                }

                var entity = await dbContext.DrmEntities
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (entity is null)
                {
                    return NotFound();
                }

                entity.IsDeleted = true;
                entity.DeletedUtc = DateTime.UtcNow;
                entity.DeletedReason = "Moved to trash from the DRM model catalogue.";

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Delete",
                    nameof(DrmEntity),
                    entity.Id,
                    $"Moved DRM entity {entity.DisplayLabel} to trash.",
                    entity.DeletedReason);

                TempData["ImportStatusMessage"] = $"Moved DRM entity {entity.DisplayLabel} to trash.";
                break;
            }
            default:
                return BadRequest();
        }

        return RedirectToAction("Index");
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreComponentAsync(int id, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        switch (modelKind)
        {
            case ReferenceModelKind.Trm:
            {
                var component = await dbContext.TrmComponents
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = false;
                component.DeletedUtc = null;
                component.DeletedReason = null;

                await dbContext.SaveChangesAsync();
                await componentVersioningService.RecordVersionAsync(component.Id, "Restored", "Restored from trash.");
                await auditLogService.WriteAsync(
                    "Component",
                    "Restore",
                    nameof(TrmComponent),
                    component.Id,
                    $"Restored TRM component {component.DisplayLabel} from trash.");

                TempData["ImportStatusMessage"] = $"Restored TRM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Arm:
            {
                var component = await dbContext.ArmComponents
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = false;
                component.DeletedUtc = null;
                component.DeletedReason = null;

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Restore",
                    nameof(ArmComponent),
                    component.Id,
                    $"Restored ARM component {component.DisplayLabel} from trash.");

                TempData["ImportStatusMessage"] = $"Restored ARM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Brm:
            {
                var component = await dbContext.BrmComponents
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                component.IsDeleted = false;
                component.DeletedUtc = null;
                component.DeletedReason = null;

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Restore",
                    nameof(BrmComponent),
                    component.Id,
                    $"Restored BRM component {component.DisplayLabel} from trash.");

                TempData["ImportStatusMessage"] = $"Restored BRM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Drm:
            {
                if (id < 0)
                {
                    var subClassId = Math.Abs(id);
                    var subClass = await dbContext.DrmCommonSubClasses
                        .FirstOrDefaultAsync(x => x.Id == subClassId && x.IsDeleted);
                    if (subClass is null)
                    {
                        return NotFound();
                    }

                    subClass.IsDeleted = false;
                    subClass.DeletedUtc = null;
                    subClass.DeletedReason = null;

                    await dbContext.SaveChangesAsync();
                    await auditLogService.WriteAsync(
                        "Component",
                        "Restore",
                        nameof(DrmCommonSubClass),
                        subClass.Id,
                        $"Restored DRM common sub-class {subClass.DisplayLabel} from trash.");

                    TempData["ImportStatusMessage"] = $"Restored DRM common sub-class {subClass.DisplayLabel}.";
                    break;
                }

                var entity = await dbContext.DrmEntities
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (entity is null)
                {
                    return NotFound();
                }

                entity.IsDeleted = false;
                entity.DeletedUtc = null;
                entity.DeletedReason = null;

                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "Restore",
                    nameof(DrmEntity),
                    entity.Id,
                    $"Restored DRM entity {entity.DisplayLabel} from trash.");

                TempData["ImportStatusMessage"] = $"Restored DRM entity {entity.DisplayLabel}.";
                break;
            }
            default:
                return BadRequest();
        }

        return RedirectToAction(GetRestoreActionName(modelKind));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentlyDeleteComponentAsync(int id, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        switch (modelKind)
        {
            case ReferenceModelKind.Trm:
            {
                var component = await dbContext.TrmComponents
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                var productMappings = await dbContext.ProductMappings
                    .Where(x => x.TrmComponentId == component.Id)
                    .ToListAsync();

                foreach (var productMapping in productMappings)
                {
                    productMapping.TrmComponentId = null;
                }

                var capabilityLinks = await dbContext.TrmComponentCapabilityLinks
                    .Where(x => x.TrmComponentId == component.Id)
                    .ToListAsync();

                dbContext.TrmComponentCapabilityLinks.RemoveRange(capabilityLinks);

                dbContext.TrmComponents.Remove(component);
                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "PermanentDelete",
                    nameof(TrmComponent),
                    component.Id,
                    $"Permanently deleted TRM component {component.DisplayLabel}.");

                TempData["ImportStatusMessage"] = $"Permanently deleted TRM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Arm:
            {
                var component = await dbContext.ArmComponents
                    .Include(x => x.CapabilityLinks)
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                // Remove all dependent ArmComponentCapabilityLinks first
                dbContext.ArmComponentCapabilityLinks.RemoveRange(component.CapabilityLinks);
                dbContext.ArmComponents.Remove(component);
                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "PermanentDelete",
                    nameof(ArmComponent),
                    component.Id,
                    $"Permanently deleted ARM component {component.DisplayLabel}.");

                TempData["ImportStatusMessage"] = $"Permanently deleted ARM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Brm:
            {
                var component = await dbContext.BrmComponents
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (component is null)
                {
                    return NotFound();
                }

                dbContext.BrmComponents.Remove(component);
                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "PermanentDelete",
                    nameof(BrmComponent),
                    component.Id,
                    $"Permanently deleted BRM component {component.DisplayLabel}.");

                TempData["ImportStatusMessage"] = $"Permanently deleted BRM component {component.DisplayLabel}.";
                break;
            }
            case ReferenceModelKind.Drm:
            {
                if (id < 0)
                {
                    var subClassId = Math.Abs(id);
                    var subClass = await dbContext.DrmCommonSubClasses
                        .FirstOrDefaultAsync(x => x.Id == subClassId && x.IsDeleted);
                    if (subClass is null)
                    {
                        return NotFound();
                    }

                    dbContext.DrmCommonSubClasses.Remove(subClass);
                    await dbContext.SaveChangesAsync();
                    await auditLogService.WriteAsync(
                        "Component",
                        "PermanentDelete",
                        nameof(DrmCommonSubClass),
                        subClass.Id,
                        $"Permanently deleted DRM common sub-class {subClass.DisplayLabel}.");

                    TempData["ImportStatusMessage"] = $"Permanently deleted DRM common sub-class {subClass.DisplayLabel}.";
                    break;
                }

                var entity = await dbContext.DrmEntities
                    .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
                if (entity is null)
                {
                    return NotFound();
                }

                dbContext.DrmEntities.Remove(entity);
                await dbContext.SaveChangesAsync();
                await auditLogService.WriteAsync(
                    "Component",
                    "PermanentDelete",
                    nameof(DrmEntity),
                    entity.Id,
                    $"Permanently deleted DRM entity {entity.DisplayLabel}.");

                TempData["ImportStatusMessage"] = $"Permanently deleted DRM entity {entity.DisplayLabel}.";
                break;
            }
            default:
                return BadRequest();
        }

        return RedirectToAction(GetRestoreActionName(modelKind));
    }

    public async Task<IActionResult> HistoryAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var component = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .ForReferenceModel(ReferenceModelKind.Trm)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (component is null)
        {
            return NotFound();
        }

        var versions = await dbContext.TrmComponentVersions
            .AsNoTracking()
            .Where(x => x.TrmComponentId == id)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync();

        return View(new ComponentHistoryViewModel
        {
            Component = component,
            Versions = versions
        });
    }

    private async Task<ReferenceCatalogueViewModel> BuildViewModelAsync(
        string? search,
        int? domainId,
        int? capabilityId,
        ReferenceModelKind? modelKind,
        string? domainCode,
        string? capabilityCode,
        string? componentCode,
        string? subClassCode,
        WorkbookImportReviewViewModel? importReview,
        string? importStatusMessage)
    {
        var trmDomains = await dbContext.TrmDomains
            .AsNoTracking()
            .ForReferenceModel(ReferenceModelKind.Trm)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var trmCapabilities = await dbContext.TrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .ForReferenceModel(ReferenceModelKind.Trm)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var trmComponents = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .ForReferenceModel(ReferenceModelKind.Trm)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var armDomains = await dbContext.ArmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync();
        var armCapabilities = await dbContext.ArmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var armComponents = await dbContext.ArmComponents
            .AsNoTracking()
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.ArmCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var brmDomains = await dbContext.BrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync();
        var brmCapabilities = await dbContext.BrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var brmComponents = await dbContext.BrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var drmTopicTypes = await dbContext.DrmTopicTypes
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync();
        var drmTopics = await dbContext.DrmTopics
            .AsNoTracking()
            .Include(x => x.TopicType)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var drmEntities = await dbContext.DrmEntities
            .AsNoTracking()
            .Include(x => x.ParentTopic)
            .ThenInclude(x => x!.TopicType)
            .Include(x => x.CommonSubClasses)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync();
        var drmSubClasses = await dbContext.DrmCommonSubClasses
            .AsNoTracking()
            .Include(x => x.ParentEntity)
            .ThenInclude(x => x!.ParentTopic)
            .ThenInclude(x => x!.TopicType)
            .Where(x => !x.IsDeleted && x.ParentEntity != null && !x.ParentEntity.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var domainDefinitions = trmDomains
            .Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Trm, domain.Id, domain.Code, domain.Name))
            .Concat(armDomains.Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Arm, domain.Id, domain.Code, domain.Name)))
            .Concat(brmDomains.Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Brm, domain.Id, domain.Code, domain.Name)))
            .Concat(drmTopicTypes.Select(topicType => new BrowserDomainDefinition(ReferenceModelKind.Drm, topicType.Id, topicType.Code, topicType.Name)))
            .OrderBy(domain => domain.ModelKind)
            .ThenBy(domain => domain.Code)
            .ToList();

        var capabilityDefinitions = trmCapabilities
            .Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Trm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode))
            .Concat(armCapabilities.Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Arm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode)))
            .Concat(brmCapabilities.Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Brm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode)))
            .Concat(drmTopics.Select(topic => new BrowserCapabilityDefinition(ReferenceModelKind.Drm, topic.Id, topic.Code, topic.Name, topic.TopicTypeCode)))
            .OrderBy(capability => capability.ModelKind)
            .ThenBy(capability => capability.Code)
            .ToList();

        var selection = NormalizeSelection(
            domainId,
            capabilityId,
            modelKind,
            domainCode,
            capabilityCode,
            componentCode,
            subClassCode,
            trmDomains,
            trmCapabilities,
            domainDefinitions,
            capabilityDefinitions);

        var allComponents = BuildTrmComponentItems(trmComponents)
            .Concat(BuildArmComponentItems(armComponents))
            .Concat(BuildBrmComponentItems(brmComponents))
            .Concat(BuildDrmComponentItems(drmEntities, drmSubClasses))
            .OrderBy(component => component.ModelKind)
            .ThenBy(component => component.IsCustom)
            .ThenBy(component => component.SecondaryCode ?? component.Code)
            .ToList();

        var normalizedSearch = NormalizeSearch(search);
        var searchTerms = NormalizeSearchTerms(normalizedSearch);
        var searchScopedComponents = allComponents
            .Where(component => MatchesSearch(component, searchTerms))
            .ToList();
        var filteredComponents = searchScopedComponents
            .Where(component => MatchesSelection(component, selection))
            .ToList();

        var modelGroups = BuildModelGroups(
            domainDefinitions,
            capabilityDefinitions,
            searchScopedComponents,
            selection,
            !string.IsNullOrWhiteSpace(normalizedSearch));
        var (selectionTitle, selectionDescription) = BuildSelectionCopy(
            selection,
            domainDefinitions,
            capabilityDefinitions,
            filteredComponents.Count,
            normalizedSearch);

        return new ReferenceCatalogueViewModel
        {
            Search = search,
            DomainId = domainId,
            CapabilityId = capabilityId,
            SelectedModelKind = selection.ModelKind,
            SelectedDomainCode = selection.DomainCode,
            SelectedCapabilityCode = selection.CapabilityCode,
            SelectedComponentCode = selection.ComponentCode,
            SelectedSubClassCode = selection.SubClassCode,
            SelectionTitle = selectionTitle,
            SelectionDescription = selectionDescription,
            ActiveTreeAnchorId = BuildAnchorId(selection),
            ModelGroups = modelGroups,
            Components = filteredComponents,
            ImportReview = importReview ?? new WorkbookImportReviewViewModel(),
            ImportStatusMessage = importStatusMessage
        };
    }

    private static BrowserSelection NormalizeSelection(
        int? legacyDomainId,
        int? legacyCapabilityId,
        ReferenceModelKind? requestedModelKind,
        string? requestedDomainCode,
        string? requestedCapabilityCode,
        string? requestedComponentCode,
        string? requestedSubClassCode,
        IReadOnlyList<TrmDomain> trmDomains,
        IReadOnlyList<TrmCapability> trmCapabilities,
        IReadOnlyList<BrowserDomainDefinition> domainDefinitions,
        IReadOnlyList<BrowserCapabilityDefinition> capabilityDefinitions)
    {
        var domainCode = NormalizeCode(requestedDomainCode);
        var capabilityCode = NormalizeCode(requestedCapabilityCode);
        var componentCode = NormalizeCode(requestedComponentCode);
        var subClassCode = NormalizeCode(requestedSubClassCode);
        var modelKind = requestedModelKind;

        if (domainCode is null && legacyDomainId.HasValue)
        {
            domainCode = trmDomains.FirstOrDefault(domain => domain.Id == legacyDomainId.Value)?.Code;
        }

        if (capabilityCode is null && legacyCapabilityId.HasValue)
        {
            capabilityCode = trmCapabilities.FirstOrDefault(capability => capability.Id == legacyCapabilityId.Value)?.Code;
        }

        modelKind ??= InferModelKind(domainCode, capabilityCode);

        if (modelKind is null && domainCode is not null)
        {
            modelKind = domainDefinitions
                .FirstOrDefault(domain => string.Equals(domain.Code, domainCode, StringComparison.OrdinalIgnoreCase))
                ?.ModelKind;
        }

        if (modelKind is null && capabilityCode is not null)
        {
            modelKind = capabilityDefinitions
                .FirstOrDefault(capability => string.Equals(capability.Code, capabilityCode, StringComparison.OrdinalIgnoreCase))
                ?.ModelKind;
        }

        if (modelKind is null && (legacyDomainId.HasValue || legacyCapabilityId.HasValue))
        {
            modelKind = ReferenceModelKind.Trm;
        }

        if (domainCode is null && capabilityCode is not null)
        {
            domainCode = capabilityDefinitions
                .FirstOrDefault(capability =>
                    capability.ModelKind == modelKind &&
                    string.Equals(capability.Code, capabilityCode, StringComparison.OrdinalIgnoreCase))
                ?.ParentDomainCode
                ?? capabilityDefinitions
                    .FirstOrDefault(capability => string.Equals(capability.Code, capabilityCode, StringComparison.OrdinalIgnoreCase))
                    ?.ParentDomainCode;
        }

        if (componentCode is not null || subClassCode is not null)
        {
            modelKind ??= ReferenceModelKind.Drm;
        }

        return new BrowserSelection(modelKind, domainCode, capabilityCode, componentCode, subClassCode);
    }

    private static List<ReferenceComponentBrowserItemViewModel> BuildTrmComponentItems(IEnumerable<TrmComponent> components) =>
        components
            .Select(component =>
            {
                var capabilities = component.CapabilityLinks
                    .Where(link => link.TrmCapability is not null)
                    .Select(link => link.TrmCapability!)
                    .DistinctBy(capability => capability.Code, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(capability => capability.Code)
                    .Select(capability => new ReferenceBrowserLabelViewModel
                    {
                        Code = capability.Code,
                        Name = capability.Name
                    })
                    .ToList();

                var domains = component.CapabilityLinks
                    .Where(link => link.TrmCapability?.ParentDomain is not null)
                    .Select(link => link.TrmCapability!.ParentDomain!)
                    .DistinctBy(domain => domain.Code, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(domain => domain.Code)
                    .Select(domain => new ReferenceBrowserLabelViewModel
                    {
                        Code = domain.Code,
                        Name = domain.Name
                    })
                    .ToList();

                return new ReferenceComponentBrowserItemViewModel
                {
                    ModelKind = ReferenceModelKind.Trm,
                    NativeId = component.Id,
                    ModelLabel = ReferenceModelCatalog.GetShortName(ReferenceModelKind.Trm),
                    Code = component.Code,
                    SecondaryCode = component.TechnologyComponentCode,
                    Name = component.Name,
                    Description = component.Description,
                    ProductExamples = component.ProductExamples,
                    TypeLabel = component.IsCustom ? "Custom" : "Model",
                    IsCustom = component.IsCustom,
                    SupportsHistory = true,
                    SupportsDelete = true,
                    Capabilities = capabilities,
                    Domains = domains
                };
            })
            .ToList();

    private static List<ReferenceComponentBrowserItemViewModel> BuildArmComponentItems(IEnumerable<ArmComponent> components) =>
        components
            .Select(component =>
            {
                var capabilities = component.CapabilityLinks
                    .Where(link => link.ArmCapability is not null)
                    .Select(link => link.ArmCapability!)
                    .DistinctBy(capability => capability.Code, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(capability => capability.Code)
                    .Select(capability => new ReferenceBrowserLabelViewModel
                    {
                        Code = capability.Code,
                        Name = capability.Name
                    })
                    .ToList();

                var domains = component.CapabilityLinks
                    .Where(link => link.ArmCapability?.ParentDomain is not null)
                    .Select(link => link.ArmCapability!.ParentDomain!)
                    .DistinctBy(domain => domain.Code, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(domain => domain.Code)
                    .Select(domain => new ReferenceBrowserLabelViewModel
                    {
                        Code = domain.Code,
                        Name = domain.Name
                    })
                    .ToList();

                return new ReferenceComponentBrowserItemViewModel
                {
                    ModelKind = ReferenceModelKind.Arm,
                    NativeId = component.Id,
                    ModelLabel = ReferenceModelCatalog.GetShortName(ReferenceModelKind.Arm),
                    Code = component.Code,
                    Name = component.Name,
                    Description = component.Description,
                    ProductExamples = component.ProductExamples,
                    TypeLabel = "Model",
                    SupportsDelete = true,
                    Capabilities = capabilities,
                    Domains = domains
                };
            })
            .ToList();

    private static List<ReferenceComponentBrowserItemViewModel> BuildBrmComponentItems(IEnumerable<BrmComponent> components) =>
        components
            .Select(component =>
            {
                var capabilities = component.ParentCapability is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = component.ParentCapability.Code,
                            Name = component.ParentCapability.Name
                        }
                    };

                var domains = component.ParentCapability?.ParentDomain is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = component.ParentCapability.ParentDomain.Code,
                            Name = component.ParentCapability.ParentDomain.Name
                        }
                    };

                return new ReferenceComponentBrowserItemViewModel
                {
                    ModelKind = ReferenceModelKind.Brm,
                    NativeId = component.Id,
                    ModelLabel = ReferenceModelCatalog.GetShortName(ReferenceModelKind.Brm),
                    Code = component.Code,
                    Name = component.Name,
                    Description = component.Description,
                    ProductExamples = component.ProductExamples,
                    TypeLabel = "Model",
                    SupportsDelete = true,
                    Capabilities = capabilities,
                    Domains = domains
                };
            })
            .ToList();

    private static List<ReferenceComponentBrowserItemViewModel> BuildDrmComponentItems(
        IEnumerable<DrmEntity> entities,
        IEnumerable<DrmCommonSubClass> subClasses)
    {
        var entityItems = entities
            .Select(entity =>
            {
                var capabilities = entity.ParentTopic is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = entity.ParentTopic.Code,
                            Name = entity.ParentTopic.Name
                        }
                    };

                var domains = entity.ParentTopic?.TopicType is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = entity.ParentTopic.TopicType.Code,
                            Name = entity.ParentTopic.TopicType.Name
                        }
                    };

                return new ReferenceComponentBrowserItemViewModel
                {
                    ModelKind = ReferenceModelKind.Drm,
                    NativeId = entity.Id,
                    ModelLabel = ReferenceModelCatalog.GetShortName(ReferenceModelKind.Drm),
                    Code = entity.Code,
                    Name = entity.Name,
                    Description = entity.Description,
                    ProductExamples = entity.AlternativeNames,
                    TypeLabel = "Entity",
                    SupportsDelete = true,
                    Capabilities = capabilities,
                    Domains = domains
                };
            });

        var subClassItems = subClasses
            .Select(subClass =>
            {
                var capabilities = subClass.ParentEntity?.ParentTopic is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = subClass.ParentEntity.ParentTopic.Code,
                            Name = subClass.ParentEntity.ParentTopic.Name
                        }
                    };

                var domains = subClass.ParentEntity?.ParentTopic?.TopicType is null
                    ? []
                    : new List<ReferenceBrowserLabelViewModel>
                    {
                        new()
                        {
                            Code = subClass.ParentEntity.ParentTopic.TopicType.Code,
                            Name = subClass.ParentEntity.ParentTopic.TopicType.Name
                        }
                    };

                return new ReferenceComponentBrowserItemViewModel
                {
                    ModelKind = ReferenceModelKind.Drm,
                    NativeId = -subClass.Id,
                    ModelLabel = ReferenceModelCatalog.GetShortName(ReferenceModelKind.Drm),
                    Code = subClass.Code,
                    ParentComponentCode = subClass.ParentEntity?.Code,
                    SecondaryCode = subClass.ParentEntity?.DisplayLabel,
                    Name = subClass.Name,
                    Description = subClass.Description,
                    ProductExamples = subClass.AlternativeNames,
                    TypeLabel = "Common sub-class",
                    SupportsDelete = true,
                    Capabilities = capabilities,
                    Domains = domains
                };
            });

        return entityItems.Concat(subClassItems).ToList();
    }

    private static List<ReferenceBrowserModelViewModel> BuildModelGroups(
        IReadOnlyList<BrowserDomainDefinition> domainDefinitions,
        IReadOnlyList<BrowserCapabilityDefinition> capabilityDefinitions,
        IReadOnlyList<ReferenceComponentBrowserItemViewModel> searchScopedComponents,
        BrowserSelection selection,
        bool limitTreeToSearch)
    {
        return ReferenceModelCatalog.All
            .Select(modelKind =>
            {
                var modelDomains = domainDefinitions
                    .Where(domain => domain.ModelKind == modelKind)
                    .OrderBy(domain => domain.Code)
                    .ToList();
                var visibleDomainCodes = searchScopedComponents
                    .Where(component => component.ModelKind == modelKind)
                    .SelectMany(component => component.Domains.Select(domain => domain.Code))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var visibleCapabilityCodes = searchScopedComponents
                    .Where(component => component.ModelKind == modelKind)
                    .SelectMany(component => component.Capabilities.Select(capability => capability.Code))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var domains = modelDomains
                    .Where(domain => !limitTreeToSearch || visibleDomainCodes.Contains(domain.Code))
                    .Select(domain =>
                    {
                        var capabilities = capabilityDefinitions
                            .Where(capability =>
                                capability.ModelKind == modelKind &&
                                string.Equals(capability.ParentDomainCode, domain.Code, StringComparison.OrdinalIgnoreCase))
                            .Where(capability => !limitTreeToSearch || visibleCapabilityCodes.Contains(capability.Code))
                            .OrderBy(capability => capability.Code)
                            .Select(capability => new ReferenceBrowserCapabilityViewModel
                            {
                                ModelKind = modelKind,
                                ParentDomainCode = domain.Code,
                                Code = capability.Code,
                                Name = capability.Name,
                                Components = modelKind == ReferenceModelKind.Drm
                                    ? BuildDrmTreeComponentNodes(searchScopedComponents, domain.Code, capability.Code, selection)
                                    : [],
                                IsSelected =
                                    selection.ModelKind == modelKind &&
                                    selection.DomainCode is not null &&
                                    selection.CapabilityCode is not null &&
                                    selection.ComponentCode is null &&
                                    selection.SubClassCode is null &&
                                    string.Equals(selection.DomainCode, domain.Code, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(selection.CapabilityCode, capability.Code, StringComparison.OrdinalIgnoreCase)
                            })
                            .ToList();

                        var hasSelectedCapability = capabilities.Any(capability =>
                            capability.IsSelected ||
                            capability.Components.Any(component => component.IsSelected || component.Children.Any(child => child.IsSelected)));

                        var isSelectedDomain =
                            selection.ModelKind == modelKind &&
                            selection.DomainCode is not null &&
                            selection.CapabilityCode is null &&
                            selection.ComponentCode is null &&
                            selection.SubClassCode is null &&
                            string.Equals(selection.DomainCode, domain.Code, StringComparison.OrdinalIgnoreCase);
                        var expandFromSearch = limitTreeToSearch && selection.ModelKind is null && capabilities.Count > 0;

                        return new ReferenceBrowserDomainViewModel
                        {
                            ModelKind = modelKind,
                            Code = domain.Code,
                            Name = domain.Name,
                            IsSelected = isSelectedDomain,
                            IsExpanded = hasSelectedCapability || isSelectedDomain || expandFromSearch,
                            Capabilities = capabilities
                        };
                    })
                    .ToList();

                var isSelectedModel =
                    selection.ModelKind == modelKind &&
                    selection.DomainCode is null &&
                    selection.CapabilityCode is null &&
                    selection.ComponentCode is null &&
                    selection.SubClassCode is null;
                var hasSelectedBranch = domains.Any(domain => domain.IsSelected || domain.IsExpanded);
                var hasVisibleContent = domains.Count > 0;

                return new ReferenceBrowserModelViewModel
                {
                    ModelKind = modelKind,
                    Label = $"{ReferenceModelCatalog.GetShortName(modelKind)} Model",
                    ShortName = ReferenceModelCatalog.GetShortName(modelKind),
                    DisplayName = ReferenceModelCatalog.GetDisplayName(modelKind),
                    DomainLabel = ReferenceModelCatalog.GetDomainLabel(modelKind),
                    IsSelected = isSelectedModel,
                    IsExpanded =
                        hasSelectedBranch ||
                        isSelectedModel ||
                        (limitTreeToSearch && selection.ModelKind is null && hasVisibleContent),
                    Domains = domains
                };
            })
            .Where(group => group.Domains.Count > 0 || group.IsSelected)
            .ToList();
    }

    private static (string Title, string Description) BuildSelectionCopy(
        BrowserSelection selection,
        IReadOnlyList<BrowserDomainDefinition> domainDefinitions,
        IReadOnlyList<BrowserCapabilityDefinition> capabilityDefinitions,
        int resultCount,
        string? normalizedSearch)
    {
        if (selection.SubClassCode is not null)
        {
            return (
                selection.SubClassCode,
                $"Showing {resultCount} result(s) from the DRM common sub-class selection{FormatSearchSuffix(normalizedSearch)}.");
        }

        if (selection.ComponentCode is not null)
        {
            return (
                selection.ComponentCode,
                $"Showing {resultCount} result(s) from the DRM entity selection{FormatSearchSuffix(normalizedSearch)}.");
        }

        if (selection.CapabilityCode is not null)
        {
            var capability = capabilityDefinitions.FirstOrDefault(item =>
                item.ModelKind == selection.ModelKind &&
                string.Equals(item.Code, selection.CapabilityCode, StringComparison.OrdinalIgnoreCase))
                ?? capabilityDefinitions.FirstOrDefault(item => string.Equals(item.Code, selection.CapabilityCode, StringComparison.OrdinalIgnoreCase));

            if (capability is not null)
            {
                return (
                    capability.DisplayLabel,
                    $"Showing {resultCount} result(s) from the {ReferenceModelCatalog.GetShortName(capability.ModelKind)} capability selection{FormatSearchSuffix(normalizedSearch)}.");
            }
        }

        if (selection.DomainCode is not null)
        {
            var domain = domainDefinitions.FirstOrDefault(item =>
                item.ModelKind == selection.ModelKind &&
                string.Equals(item.Code, selection.DomainCode, StringComparison.OrdinalIgnoreCase))
                ?? domainDefinitions.FirstOrDefault(item => string.Equals(item.Code, selection.DomainCode, StringComparison.OrdinalIgnoreCase));

            if (domain is not null)
            {
                return (
                    domain.DisplayLabel,
                    $"Showing {resultCount} result(s) from the {ReferenceModelCatalog.GetShortName(domain.ModelKind)} domain selection{FormatSearchSuffix(normalizedSearch)}.");
            }
        }

        if (selection.ModelKind.HasValue)
        {
            return (
                $"{ReferenceModelCatalog.GetShortName(selection.ModelKind.Value)} Model",
                $"Showing {resultCount} result(s) from the {ReferenceModelCatalog.GetDisplayName(selection.ModelKind.Value)}{FormatSearchSuffix(normalizedSearch)}.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return (
                "Search results",
                $"Showing {resultCount} result(s) across TRM, ARM, BRM, and DRM for \"{normalizedSearch}\".");
        }

        return (
            "All reference models",
            $"Showing {resultCount} imported result(s) across TRM, ARM, BRM, and DRM.");
    }

    private static List<ReferenceBrowserComponentNodeViewModel> BuildDrmTreeComponentNodes(
        IReadOnlyList<ReferenceComponentBrowserItemViewModel> components,
        string domainCode,
        string capabilityCode,
        BrowserSelection selection)
    {
        var drmComponents = components
            .Where(component =>
                component.ModelKind == ReferenceModelKind.Drm &&
                component.Domains.Any(domain => string.Equals(domain.Code, domainCode, StringComparison.OrdinalIgnoreCase)) &&
                component.Capabilities.Any(capability => string.Equals(capability.Code, capabilityCode, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var subClasses = drmComponents
            .Where(component => string.Equals(component.TypeLabel, "Common sub-class", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return drmComponents
            .Where(component => string.Equals(component.TypeLabel, "Entity", StringComparison.OrdinalIgnoreCase))
            .OrderBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
            .Select(entity => new ReferenceBrowserComponentNodeViewModel
            {
                ModelKind = ReferenceModelKind.Drm,
                ParentDomainCode = domainCode,
                ParentCapabilityCode = capabilityCode,
                Code = entity.Code,
                Name = entity.Name,
                IsSelected =
                    selection.ModelKind == ReferenceModelKind.Drm &&
                    selection.SubClassCode is null &&
                    string.Equals(selection.DomainCode, domainCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(selection.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(selection.ComponentCode, entity.Code, StringComparison.OrdinalIgnoreCase),
                Children = subClasses
                    .Where(subClass => string.Equals(subClass.ParentComponentCode, entity.Code, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(subClass => subClass.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(subClass => new ReferenceBrowserComponentNodeViewModel
                    {
                        ModelKind = ReferenceModelKind.Drm,
                        ParentDomainCode = domainCode,
                        ParentCapabilityCode = capabilityCode,
                        ParentComponentCode = entity.Code,
                        Code = subClass.Code,
                        Name = subClass.Name,
                        IsSelected =
                            selection.ModelKind == ReferenceModelKind.Drm &&
                            string.Equals(selection.DomainCode, domainCode, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(selection.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(selection.ComponentCode, entity.Code, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(selection.SubClassCode, subClass.Code, StringComparison.OrdinalIgnoreCase)
                    })
                    .ToList()
            })
            .ToList();
    }

    private static bool MatchesSelection(ReferenceComponentBrowserItemViewModel component, BrowserSelection selection)
    {
        if (selection.ModelKind.HasValue && component.ModelKind != selection.ModelKind.Value)
        {
            return false;
        }

        if (selection.DomainCode is not null &&
            !component.Domains.Any(domain => string.Equals(domain.Code, selection.DomainCode, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (selection.CapabilityCode is not null &&
            !component.Capabilities.Any(capability => string.Equals(capability.Code, selection.CapabilityCode, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (selection.SubClassCode is not null)
        {
            return component.ModelKind == ReferenceModelKind.Drm &&
                string.Equals(component.TypeLabel, "Common sub-class", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(component.Code, selection.SubClassCode, StringComparison.OrdinalIgnoreCase) &&
                (selection.ComponentCode is null ||
                    string.Equals(component.ParentComponentCode, selection.ComponentCode, StringComparison.OrdinalIgnoreCase));
        }

        if (selection.ComponentCode is not null)
        {
            if (component.ModelKind != ReferenceModelKind.Drm)
            {
                return string.Equals(component.Code, selection.ComponentCode, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(component.Code, selection.ComponentCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(component.ParentComponentCode, selection.ComponentCode, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool MatchesSearch(ReferenceComponentBrowserItemViewModel component, string[] searchTerms)
    {
        if (searchTerms.Length == 0)
        {
            return true;
        }

        var searchableText = BuildSearchText(component);
        return searchTerms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    private static string[] NormalizeSearchTerms(string? normalizedSearch) =>
        string.IsNullOrWhiteSpace(normalizedSearch)
            ? []
            : normalizedSearch
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    private static string BuildSearchText(ReferenceComponentBrowserItemViewModel component)
    {
        var values = new List<string?>
        {
            component.ModelLabel,
            ReferenceModelCatalog.GetDisplayName(component.ModelKind),
            component.Code,
            component.SecondaryCode,
            component.Name,
            component.Description,
            component.ProductExamples,
            component.TypeLabel
        };

        values.AddRange(component.Capabilities.SelectMany(capability => new[] { capability.Code, capability.Name }));
        values.AddRange(component.Domains.SelectMany(domain => new[] { domain.Code, domain.Name }));

        return string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static ReferenceModelKind? InferModelKind(string? domainCode, string? capabilityCode)
    {
        foreach (var modelKind in ReferenceModelCatalog.All)
        {
            if (!string.IsNullOrWhiteSpace(domainCode) &&
                domainCode.StartsWith(ReferenceModelCatalog.GetDomainPrefix(modelKind), StringComparison.OrdinalIgnoreCase))
            {
                return modelKind;
            }

            if (!string.IsNullOrWhiteSpace(capabilityCode) &&
                capabilityCode.StartsWith(ReferenceModelCatalog.GetCapabilityPrefix(modelKind), StringComparison.OrdinalIgnoreCase))
            {
                return modelKind;
            }
        }

        return null;
    }

    private static string FormatSearchSuffix(string? normalizedSearch) =>
        string.IsNullOrWhiteSpace(normalizedSearch)
            ? "."
            : $" with search \"{normalizedSearch}\".";

    private async Task<ReferenceRestoreViewModel> BuildRestoreViewModelAsync(
        ReferenceModelKind modelKind,
        string? statusMessage)
    {
        var items = modelKind switch
        {
            ReferenceModelKind.Trm => (await dbContext.TrmComponents
                    .AsNoTracking()
                    .Include(x => x.CapabilityLinks)
                    .ThenInclude(x => x.TrmCapability)
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .Where(x => x.IsDeleted)
                    .OrderByDescending(x => x.DeletedUtc)
                    .ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(component => new ReferenceRestoreItemViewModel
                {
                    ModelKind = ReferenceModelKind.Trm,
                    Id = component.Id,
                    DisplayLabel = component.DisplayLabel,
                    CapabilitiesText = BuildCapabilitiesText(component.CapabilityLinks
                        .Where(link => link.TrmCapability != null)
                        .Select(link => $"{link.TrmCapability!.Code} {link.TrmCapability.Name}")),
                    DeletedUtc = component.DeletedUtc,
                    DeletedReason = component.DeletedReason,
                    SupportsHistory = true
                })
                .ToList(),
            ReferenceModelKind.Arm => (await dbContext.ArmComponents
                    .AsNoTracking()
                    .Include(x => x.CapabilityLinks)
                    .ThenInclude(x => x.ArmCapability)
                    .Where(x => x.IsDeleted)
                    .OrderByDescending(x => x.DeletedUtc)
                    .ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(component => new ReferenceRestoreItemViewModel
                {
                    ModelKind = ReferenceModelKind.Arm,
                    Id = component.Id,
                    DisplayLabel = component.DisplayLabel,
                    CapabilitiesText = BuildCapabilitiesText(component.CapabilityLinks
                        .Where(link => link.ArmCapability != null)
                        .Select(link => $"{link.ArmCapability!.Code} {link.ArmCapability.Name}")),
                    DeletedUtc = component.DeletedUtc,
                    DeletedReason = component.DeletedReason
                })
                .ToList(),
            ReferenceModelKind.Brm => (await dbContext.BrmComponents
                    .AsNoTracking()
                    .Include(x => x.ParentCapability)
                    .Where(x => x.IsDeleted)
                    .OrderByDescending(x => x.DeletedUtc)
                    .ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(component => new ReferenceRestoreItemViewModel
                {
                    ModelKind = ReferenceModelKind.Brm,
                    Id = component.Id,
                    DisplayLabel = component.DisplayLabel,
                    CapabilitiesText = component.ParentCapability != null
                        ? $"{component.ParentCapability.Code} {component.ParentCapability.Name}"
                        : "-",
                    DeletedUtc = component.DeletedUtc,
                    DeletedReason = component.DeletedReason
                })
                .ToList(),
            ReferenceModelKind.Drm => (await dbContext.DrmEntities
                    .AsNoTracking()
                    .Include(x => x.ParentTopic)
                    .Where(x => x.IsDeleted)
                    .OrderByDescending(x => x.DeletedUtc)
                    .ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(entity => new ReferenceRestoreItemViewModel
                {
                    ModelKind = ReferenceModelKind.Drm,
                    Id = entity.Id,
                    DisplayLabel = entity.DisplayLabel,
                    CapabilitiesText = entity.ParentTopic != null
                        ? $"{entity.ParentTopic.Code} {entity.ParentTopic.Name}"
                        : "-",
                    DeletedUtc = entity.DeletedUtc,
                    DeletedReason = entity.DeletedReason
                })
                .Concat((await dbContext.DrmCommonSubClasses
                    .AsNoTracking()
                    .Include(x => x.ParentEntity)
                    .Where(x => x.IsDeleted)
                    .OrderByDescending(x => x.DeletedUtc)
                    .ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(subClass => new ReferenceRestoreItemViewModel
                {
                    ModelKind = ReferenceModelKind.Drm,
                    Id = -subClass.Id,
                    DisplayLabel = subClass.DisplayLabel,
                    CapabilitiesText = subClass.ParentEntity != null
                        ? $"{subClass.ParentEntity.Code} {subClass.ParentEntity.Name}"
                        : "-",
                    DeletedUtc = subClass.DeletedUtc,
                    DeletedReason = subClass.DeletedReason
                }))
                .ToList(),
            _ => []
        };

        var shortName = ReferenceModelCatalog.GetShortName(modelKind);

        return new ReferenceRestoreViewModel
        {
            ModelKind = modelKind,
            PageTitle = $"Restore {shortName} model objects",
            Eyebrow = $"Deleted HERM {shortName} model objects",
            Heading = $"Restore {shortName} model objects",
            Description = "Restore model objects back into the catalogue or permanently delete them.",
            EmptyHeading = $"No deleted {shortName} model objects",
            EmptyDescription = $"The {shortName} model trash is empty.",
            AdminNavKey = GetAdminNavKey(modelKind),
            Components = items,
            StatusMessage = statusMessage
        };
    }

    private static string BuildAnchorId(BrowserSelection selection)
    {
        if (!selection.ModelKind.HasValue)
        {
            return "browser-navigation";
        }

        var modelAnchor = $"browser-model-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(ReferenceModelCatalog.GetShortName(selection.ModelKind.Value))}";
        if (string.IsNullOrWhiteSpace(selection.DomainCode))
        {
            return modelAnchor;
        }

        var domainAnchor = $"{modelAnchor}-domain-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(selection.DomainCode)}";
        if (string.IsNullOrWhiteSpace(selection.CapabilityCode))
        {
            return domainAnchor;
        }

        var capabilityAnchor = $"{domainAnchor}-capability-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(selection.CapabilityCode)}";
        if (string.IsNullOrWhiteSpace(selection.ComponentCode))
        {
            return capabilityAnchor;
        }

        var componentAnchor = $"{capabilityAnchor}-component-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(selection.ComponentCode)}";
        if (string.IsNullOrWhiteSpace(selection.SubClassCode))
        {
            return componentAnchor;
        }

        return $"{componentAnchor}-subclass-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(selection.SubClassCode)}";
    }

    private static string GetRestoreActionName(ReferenceModelKind modelKind) => modelKind switch
    {
        ReferenceModelKind.Arm => "RestoreArm",
        ReferenceModelKind.Brm => "RestoreBrm",
        ReferenceModelKind.Drm => "RestoreDrm",
        _ => "Restore"
    };

    private static string GetAdminNavKey(ReferenceModelKind modelKind) => modelKind switch
    {
        ReferenceModelKind.Arm => "RestoreArmModelObjects",
        ReferenceModelKind.Brm => "RestoreBrmModelObjects",
        ReferenceModelKind.Drm => "RestoreDrmModelObjects",
        _ => "RestoreTrmModelObjects"
    };

    private static string BuildCapabilitiesText(IEnumerable<string> values)
    {
        var formattedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value)
            .ToList();

        return formattedValues.Count == 0 ? "-" : string.Join(", ", formattedValues);
    }

    private string EnsurePendingImportDirectory()
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "PendingImports");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record BrowserSelection(
        ReferenceModelKind? ModelKind,
        string? DomainCode,
        string? CapabilityCode,
        string? ComponentCode,
        string? SubClassCode);

    private sealed record BrowserDomainDefinition(ReferenceModelKind ModelKind, int NativeId, string Code, string Name)
    {
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
            ? Name
            : $"{Code} {Name}";
    }

    private sealed record BrowserCapabilityDefinition(ReferenceModelKind ModelKind, int NativeId, string Code, string Name, string? ParentDomainCode)
    {
        public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
            ? Name
            : $"{Code} {Name}";
    }
}
