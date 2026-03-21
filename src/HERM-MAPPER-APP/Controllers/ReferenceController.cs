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
        string? capabilityCode = null)
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
            null,
            TempData["ImportStatusMessage"] as string));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> RestoreAsync()
    {
        var components = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .ForReferenceModel(ReferenceModelKind.Trm)
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedUtc)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return View("Restore", new ReferenceRestoreViewModel
        {
            Components = components,
            StatusMessage = TempData["ImportStatusMessage"] as string
        });
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
    public async Task<IActionResult> DeleteComponentAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var component = await dbContext.TrmComponents
            .ForReferenceModel(ReferenceModelKind.Trm)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (component is null)
        {
            return NotFound();
        }

        component.IsDeleted = true;
        component.DeletedUtc = DateTime.UtcNow;
        component.DeletedReason = "Moved to trash from the reference catalogue.";

        await dbContext.SaveChangesAsync();
        await componentVersioningService.RecordVersionAsync(component.Id, "Deleted", component.DeletedReason);
        await auditLogService.WriteAsync(
            "Component",
            "Delete",
            nameof(TrmComponent),
            component.Id,
            $"Moved component {component.DisplayLabel} to trash.",
            component.DeletedReason);

        TempData["ImportStatusMessage"] = $"Moved component {component.DisplayLabel} to trash.";
        return RedirectToAction("Index");
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreComponentAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

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
            $"Restored component {component.DisplayLabel} from trash.");

        TempData["ImportStatusMessage"] = $"Restored component {component.DisplayLabel}.";
        return RedirectToAction("Restore");
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentlyDeleteComponentAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var component = await dbContext.TrmComponents
            .ForReferenceModel(ReferenceModelKind.Trm)
            .FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (component is null)
        {
            return NotFound();
        }

        dbContext.TrmComponents.Remove(component);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Component",
            "PermanentDelete",
            nameof(TrmComponent),
            component.Id,
            $"Permanently deleted component {component.DisplayLabel}.");

        TempData["ImportStatusMessage"] = $"Permanently deleted component {component.DisplayLabel}.";
        return RedirectToAction("Restore");
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
            .OrderBy(x => x.Code)
            .ToListAsync();

        var domainDefinitions = trmDomains
            .Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Trm, domain.Id, domain.Code, domain.Name))
            .Concat(armDomains.Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Arm, domain.Id, domain.Code, domain.Name)))
            .Concat(brmDomains.Select(domain => new BrowserDomainDefinition(ReferenceModelKind.Brm, domain.Id, domain.Code, domain.Name)))
            .OrderBy(domain => domain.ModelKind)
            .ThenBy(domain => domain.Code)
            .ToList();

        var capabilityDefinitions = trmCapabilities
            .Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Trm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode))
            .Concat(armCapabilities.Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Arm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode)))
            .Concat(brmCapabilities.Select(capability => new BrowserCapabilityDefinition(ReferenceModelKind.Brm, capability.Id, capability.Code, capability.Name, capability.ParentDomainCode)))
            .OrderBy(capability => capability.ModelKind)
            .ThenBy(capability => capability.Code)
            .ToList();

        var selection = NormalizeSelection(
            domainId,
            capabilityId,
            modelKind,
            domainCode,
            capabilityCode,
            trmDomains,
            trmCapabilities,
            domainDefinitions,
            capabilityDefinitions);

        var allComponents = BuildTrmComponentItems(trmComponents)
            .Concat(BuildArmComponentItems(armComponents))
            .Concat(BuildBrmComponentItems(brmComponents))
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
            allComponents,
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
        IReadOnlyList<TrmDomain> trmDomains,
        IReadOnlyList<TrmCapability> trmCapabilities,
        IReadOnlyList<BrowserDomainDefinition> domainDefinitions,
        IReadOnlyList<BrowserCapabilityDefinition> capabilityDefinitions)
    {
        var domainCode = NormalizeCode(requestedDomainCode);
        var capabilityCode = NormalizeCode(requestedCapabilityCode);
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

        return new BrowserSelection(modelKind, domainCode, capabilityCode);
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
                    Capabilities = capabilities,
                    Domains = domains
                };
            })
            .ToList();

    private static List<ReferenceBrowserModelViewModel> BuildModelGroups(
        IReadOnlyList<BrowserDomainDefinition> domainDefinitions,
        IReadOnlyList<BrowserCapabilityDefinition> capabilityDefinitions,
        IReadOnlyList<ReferenceComponentBrowserItemViewModel> allComponents,
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
                            .OrderBy(capability => capability.Code)
                            .Where(capability => !limitTreeToSearch || visibleCapabilityCodes.Contains(capability.Code))
                            .Select(capability => new ReferenceBrowserCapabilityViewModel
                            {
                                ModelKind = modelKind,
                                ParentDomainCode = domain.Code,
                                Code = capability.Code,
                                Name = capability.Name,
                                IsSelected =
                                    selection.ModelKind == modelKind &&
                                    selection.DomainCode is not null &&
                                    selection.CapabilityCode is not null &&
                                    string.Equals(selection.DomainCode, domain.Code, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(selection.CapabilityCode, capability.Code, StringComparison.OrdinalIgnoreCase)
                            })
                            .ToList();

                        var hasSelectedCapability = capabilities.Any(capability => capability.IsSelected);

                        var isSelectedDomain =
                            selection.ModelKind == modelKind &&
                            selection.DomainCode is not null &&
                            selection.CapabilityCode is null &&
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
                    selection.CapabilityCode is null;
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
                $"Showing {resultCount} result(s) across TRM, ARM, and BRM for \"{normalizedSearch}\".");
        }

        return (
            "All reference models",
            $"Showing {resultCount} imported result(s) across TRM, ARM, and BRM.");
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

        return true;
    }

    private static bool MatchesSearch(ReferenceComponentBrowserItemViewModel component, IReadOnlyList<string> searchTerms)
    {
        if (searchTerms.Count == 0)
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

        return $"{domainAnchor}-capability-{ReferenceBrowserAnchorUtility.NormalizeAnchorSegment(selection.CapabilityCode)}";
    }

    private string EnsurePendingImportDirectory()
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "PendingImports");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record BrowserSelection(ReferenceModelKind? ModelKind, string? DomainCode, string? CapabilityCode);

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
