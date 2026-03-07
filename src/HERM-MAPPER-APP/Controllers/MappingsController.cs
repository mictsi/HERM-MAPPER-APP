using System.Text;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class MappingsController(
    AppDbContext dbContext,
    CsvExportService csvExportService,
    AuditLogService auditLogService,
    ComponentVersioningService componentVersioningService,
    ConfigurableFieldService configurableFieldService) : Controller
{
    public async Task<IActionResult> Index(string? search, MappingStatus? status, int? domainId, int? capabilityId)
    {
        var productsQuery = dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmDomain)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmCapability)
            .Include(x => x.Mappings)
            .ThenInclude(x => x.TrmComponent)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            productsQuery = productsQuery.Where(x =>
                x.Name.Contains(search) ||
                (x.Vendor != null && x.Vendor.Contains(search)) ||
                x.Mappings.Any(m => m.TrmComponent != null &&
                    (m.TrmComponent.Name.Contains(search) ||
                     m.TrmComponent.Code.Contains(search) ||
                     (m.TrmComponent.TechnologyComponentCode != null && m.TrmComponent.TechnologyComponentCode.Contains(search)))));
        }

        if (status.HasValue)
        {
            productsQuery = productsQuery.Where(x => x.Mappings.Any(m => m.MappingStatus == status.Value));
        }

        if (domainId.HasValue)
        {
            productsQuery = productsQuery.Where(x => x.Mappings.Any(m => m.TrmDomainId == domainId));
        }

        if (capabilityId.HasValue)
        {
            productsQuery = productsQuery.Where(x => x.Mappings.Any(m => m.TrmCapabilityId == capabilityId));
        }

        var model = new MappingBoardViewModel
        {
            Search = search,
            Status = status,
            DomainId = domainId,
            CapabilityId = capabilityId,
            DraftCount = await dbContext.ProductMappings.CountAsync(x => x.MappingStatus == MappingStatus.Draft),
            InReviewCount = await dbContext.ProductMappings.CountAsync(x => x.MappingStatus == MappingStatus.InReview),
            CompleteCount = await dbContext.ProductMappings.CountAsync(x => x.MappingStatus == MappingStatus.Complete),
            OutOfScopeCount = await dbContext.ProductMappings.CountAsync(x => x.MappingStatus == MappingStatus.OutOfScope),
            Domains = await dbContext.TrmDomains.AsNoTracking().OrderBy(x => x.Code).ToListAsync(),
            Capabilities = await BuildCapabilityFilter(domainId),
            Products = await productsQuery.OrderBy(x => x.Name).ToListAsync()
        };

        return View(model);
    }

    public async Task<IActionResult> Create(int productId)
    {
        var product = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Include(x => x.Owners)
            .FirstOrDefaultAsync(x => x.Id == productId);
        if (product is null)
        {
            return NotFound();
        }

        return View("Edit", await BuildMappingEditViewModel(product, (ProductMapping?)null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MappingEditViewModel model)
    {
        var product = await dbContext.ProductCatalogItems
            .Include(x => x.Owners)
            .FirstOrDefaultAsync(x => x.Id == model.ProductId);
        if (product is null)
        {
            return NotFound();
        }

        model.Owners = NormalizeSelections(model.Owners);

        var mapping = new ProductMapping
        {
            ProductCatalogItemId = product.Id,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        var mappingUpdate = await PopulateAndValidateMapping(model, mapping);
        if (mappingUpdate is null)
        {
            return View("Edit", await BuildMappingEditViewModel(product, model));
        }

        dbContext.ProductMappings.Add(mapping);
        SynchronizeOwners(product, model.Owners);
        product.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        await WriteMappingAuditAsync(mapping, "Create", "Created mapping.");
        await WriteCustomComponentHistoryAsync(mappingUpdate, mapping.TrmComponent);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var mapping = await dbContext.ProductMappings
            .AsNoTracking()
            .Include(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Owners)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (mapping?.ProductCatalogItem is null)
        {
            return NotFound();
        }

        return View(await BuildMappingEditViewModel(mapping.ProductCatalogItem, mapping));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MappingEditViewModel model)
    {
        var mapping = await dbContext.ProductMappings
            .Include(x => x.ProductCatalogItem)
            .ThenInclude(x => x!.Owners)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (mapping?.ProductCatalogItem is null)
        {
            return NotFound();
        }

        model.Owners = NormalizeSelections(model.Owners);
        var mappingUpdate = await PopulateAndValidateMapping(model, mapping);
        if (mappingUpdate is null)
        {
            return View(await BuildMappingEditViewModel(mapping.ProductCatalogItem, model, mapping.Id));
        }

        mapping.UpdatedUtc = DateTime.UtcNow;
        SynchronizeOwners(mapping.ProductCatalogItem, model.Owners);
        mapping.ProductCatalogItem.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        await WriteMappingAuditAsync(mapping, "Update", "Updated mapping.");
        await WriteCustomComponentHistoryAsync(mappingUpdate, mapping.TrmComponent);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var mapping = await dbContext.ProductMappings
            .AsNoTracking()
            .Include(x => x.ProductCatalogItem)
            .Include(x => x.TrmDomain)
            .Include(x => x.TrmCapability)
            .Include(x => x.TrmComponent)
            .FirstOrDefaultAsync(x => x.Id == id);

        return mapping is null ? NotFound() : View(mapping);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var mapping = await dbContext.ProductMappings
            .Include(x => x.ProductCatalogItem)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (mapping is null)
        {
            return NotFound();
        }

        if (mapping.ProductCatalogItem is not null)
        {
            mapping.ProductCatalogItem.UpdatedUtc = DateTime.UtcNow;
        }

        dbContext.ProductMappings.Remove(mapping);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Mapping",
            "Delete",
            nameof(ProductMapping),
            id,
            $"Deleted mapping for {mapping.ProductCatalogItem?.Name ?? "product"}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Capabilities(int? domainId)
    {
        var query = dbContext.TrmCapabilities.AsNoTracking().AsQueryable();
        if (domainId.HasValue)
        {
            query = query.Where(x => x.ParentDomainId == domainId);
        }

        var capabilities = await query
            .OrderBy(x => x.Code)
            .Select(x => new { id = x.Id, text = x.Code + " " + x.Name })
            .ToListAsync();

        return Json(capabilities);
    }

    [HttpGet]
    public async Task<IActionResult> Components(int? capabilityId)
    {
        var query = dbContext.TrmComponents.AsNoTracking().AsQueryable();
        if (capabilityId.HasValue)
        {
            query = query.Where(x => !x.IsDeleted && x.CapabilityLinks.Any(link => link.TrmCapabilityId == capabilityId));
        }
        else
        {
            query = query.Where(x => !x.IsDeleted);
        }

        var components = await query
            .OrderBy(x => x.IsCustom)
            .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
            .Select(x => new { id = x.Id, text = (x.IsCustom ? (x.TechnologyComponentCode ?? x.Code) : x.Code) + " " + x.Name })
            .ToListAsync();

        return Json(components);
    }

    [HttpGet]
    public async Task<FileResult> ExportCsv(string? search, MappingStatus? status, int? domainId, int? capabilityId, bool includeUnfinished = false)
    {
        var mappings = await BuildExportQuery(search, status, domainId, capabilityId, includeUnfinished)
            .OrderBy(x => x.TrmDomain != null ? x.TrmDomain.Name : string.Empty)
            .ThenBy(x => x.TrmComponent != null ? x.TrmComponent.Name : string.Empty)
            .ThenBy(x => x.ProductCatalogItem != null ? x.ProductCatalogItem.Name : string.Empty)
            .ToListAsync();

        var csv = csvExportService.BuildProductMappingExport(mappings);
        var fileName = $"herm-mappings-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }

    private IQueryable<ProductMapping> BuildExportQuery(string? search, MappingStatus? status, int? domainId, int? capabilityId, bool includeUnfinished)
    {
        var query = dbContext.ProductMappings
            .AsNoTracking()
            .Include(x => x.ProductCatalogItem)
            .Include(x => x.TrmDomain)
            .Include(x => x.TrmCapability)
            .Include(x => x.TrmComponent)
            .AsQueryable();

        if (!includeUnfinished)
        {
            query = query.Where(x => x.MappingStatus == MappingStatus.Complete && x.TrmComponentId != null);
        }
        else if (status.HasValue)
        {
            query = query.Where(x => x.MappingStatus == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                (x.ProductCatalogItem != null && x.ProductCatalogItem.Name.Contains(search)) ||
                (x.TrmComponent != null &&
                    (x.TrmComponent.Name.Contains(search) ||
                     x.TrmComponent.Code.Contains(search) ||
                     (x.TrmComponent.TechnologyComponentCode != null && x.TrmComponent.TechnologyComponentCode.Contains(search)))));
        }

        if (domainId.HasValue)
        {
            query = query.Where(x => x.TrmDomainId == domainId);
        }

        if (capabilityId.HasValue)
        {
            query = query.Where(x => x.TrmCapabilityId == capabilityId);
        }

        return query;
    }

    private async Task<MappingUpdateResult?> PopulateAndValidateMapping(MappingEditViewModel model, ProductMapping mapping)
    {
        TrmDomain? domain = null;
        TrmCapability? capability = null;
        TrmComponent? component = null;
        var mappingUpdate = new MappingUpdateResult();
        var customTechnologyComponentCode = NormalizeInput(model.CustomTechnologyComponentCode);
        var customComponentName = NormalizeInput(model.CustomComponentName);

        if (model.SelectedComponentId.HasValue &&
            (!string.IsNullOrWhiteSpace(customTechnologyComponentCode) || !string.IsNullOrWhiteSpace(customComponentName)))
        {
            ModelState.AddModelError(nameof(model.SelectedComponentId), "Choose an existing component or enter a custom component, not both.");
        }

        if (string.IsNullOrWhiteSpace(customTechnologyComponentCode) != string.IsNullOrWhiteSpace(customComponentName))
        {
            ModelState.AddModelError(nameof(model.CustomTechnologyComponentCode), "A custom component needs both a Technology Component Code and a custom component name.");
        }

        if (!string.IsNullOrWhiteSpace(customTechnologyComponentCode) && !string.IsNullOrWhiteSpace(customComponentName))
        {
            if (!model.SelectedCapabilityId.HasValue)
            {
                ModelState.AddModelError(nameof(model.SelectedCapabilityId), "Choose a TRM capability before adding a custom component.");
            }
            else
            {
                capability = await dbContext.TrmCapabilities
                    .Include(x => x.ParentDomain)
                    .FirstOrDefaultAsync(x => x.Id == model.SelectedCapabilityId.Value);

                if (capability is null)
                {
                    ModelState.AddModelError(nameof(model.SelectedCapabilityId), "Choose a valid HERM TRM capability.");
                }
                else
                {
                    domain = capability.ParentDomain;
                    var customComponentResult = await ResolveOrCreateCustomComponentAsync(capability, customTechnologyComponentCode, customComponentName);
                    if (customComponentResult.ErrorMessage is not null)
                    {
                        ModelState.AddModelError(nameof(model.CustomTechnologyComponentCode), customComponentResult.ErrorMessage);
                    }
                    else
                    {
                        component = customComponentResult.Component;
                        mappingUpdate = new MappingUpdateResult(customComponentResult.WasCreated, customComponentResult.WasChanged);
                    }
                }
            }
        }
        else if (model.SelectedComponentId.HasValue)
        {
            component = await dbContext.TrmComponents
                .Include(x => x.CapabilityLinks)
                .ThenInclude(x => x.TrmCapability)
                .ThenInclude(x => x!.ParentDomain)
                .FirstOrDefaultAsync(x => x.Id == model.SelectedComponentId.Value && !x.IsDeleted);

            if (component is null)
            {
                ModelState.AddModelError(nameof(model.SelectedComponentId), "Choose a valid HERM TRM component.");
            }
            else
            {
                capability = model.SelectedCapabilityId.HasValue
                    ? component.CapabilityLinks
                        .Where(x => x.TrmCapabilityId == model.SelectedCapabilityId.Value)
                        .Select(x => x.TrmCapability)
                        .FirstOrDefault()
                    : component.CapabilityLinks
                        .Select(x => x.TrmCapability)
                        .FirstOrDefault();

                if (model.SelectedCapabilityId.HasValue && capability is null)
                {
                    ModelState.AddModelError(nameof(model.SelectedCapabilityId), "The selected component is not linked to that TRM capability.");
                }

                domain = capability?.ParentDomain;
            }
        }
        else if (model.SelectedCapabilityId.HasValue)
        {
            capability = await dbContext.TrmCapabilities
                .Include(x => x.ParentDomain)
                .FirstOrDefaultAsync(x => x.Id == model.SelectedCapabilityId.Value);

            if (capability is null)
            {
                ModelState.AddModelError(nameof(model.SelectedCapabilityId), "Choose a valid HERM TRM capability.");
            }
            else
            {
                domain = capability.ParentDomain;
            }
        }
        else if (model.SelectedDomainId.HasValue)
        {
            domain = await dbContext.TrmDomains.FirstOrDefaultAsync(x => x.Id == model.SelectedDomainId.Value);
            if (domain is null)
            {
                ModelState.AddModelError(nameof(model.SelectedDomainId), "Choose a valid HERM TRM domain.");
            }
        }

        if (model.MappingStatus == MappingStatus.Complete && component is null)
        {
            ModelState.AddModelError(nameof(model.SelectedComponentId), "A completed mapping must select a HERM TRM component.");
        }

        if (!ModelState.IsValid)
        {
            return null;
        }

        mapping.TrmDomainId = domain?.Id;
        mapping.TrmCapabilityId = capability?.Id;
        mapping.TrmComponentId = component?.Id;
        mapping.TrmDomain = domain;
        mapping.TrmCapability = capability;
        mapping.TrmComponent = component;
        mapping.MappingStatus = model.MappingStatus;
        mapping.MappingRationale = model.MappingRationale;
        mapping.LastReviewedUtc = DateTime.UtcNow;
        return mappingUpdate;
    }

    private async Task<IReadOnlyList<TrmCapability>> BuildCapabilityFilter(int? domainId)
    {
        var query = dbContext.TrmCapabilities.AsNoTracking().AsQueryable();
        if (domainId.HasValue)
        {
            query = query.Where(x => x.ParentDomainId == domainId);
        }

        return await query.OrderBy(x => x.Code).ToListAsync();
    }

    private async Task<MappingEditViewModel> BuildMappingEditViewModel(ProductCatalogItem product, ProductMapping? mapping, int? mappingIdOverride = null)
    {
        var selectedDomainId = mapping?.TrmDomainId;
        var selectedCapabilityId = mapping?.TrmCapabilityId;
        var selectedComponentId = mapping?.TrmComponentId;

        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
            .ToListAsync();

        var capabilitiesQuery = dbContext.TrmCapabilities.AsNoTracking().OrderBy(x => x.Code).AsQueryable();
        if (selectedDomainId.HasValue)
        {
            capabilitiesQuery = capabilitiesQuery.Where(x => x.ParentDomainId == selectedDomainId);
        }

        var capabilities = await capabilitiesQuery
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
            .ToListAsync();

        var componentsQuery = dbContext.TrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .AsQueryable();
        if (selectedCapabilityId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.CapabilityLinks.Any(link => link.TrmCapabilityId == selectedCapabilityId));
        }

        var components = await componentsQuery
            .OrderBy(x => x.IsCustom)
            .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
            .Select(x => new SelectListItem((x.IsCustom ? (x.TechnologyComponentCode ?? x.Code) : x.Code) + " " + x.Name, x.Id.ToString()))
            .ToListAsync();

        return new MappingEditViewModel
        {
            MappingId = mappingIdOverride ?? mapping?.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            Vendor = product.Vendor,
            Version = product.Version,
            Description = product.Description,
            LifecycleStatus = product.LifecycleStatus,
            Owners = product.OwnerValues.ToList(),
            SelectedDomainId = selectedDomainId,
            SelectedCapabilityId = selectedCapabilityId,
            SelectedComponentId = selectedComponentId,
            MappingStatus = mapping?.MappingStatus ?? MappingStatus.Draft,
            MappingRationale = mapping?.MappingRationale,
            Domains = domains,
            Capabilities = capabilities,
            Components = components,
            OwnerOptions = await configurableFieldService.GetMultiSelectListAsync(ConfigurableFieldNames.Owner, product.OwnerValues)
        };
    }

    private async Task<MappingEditViewModel> BuildMappingEditViewModel(ProductCatalogItem product, MappingEditViewModel postedModel, int? mappingIdOverride = null)
    {
        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
            .ToListAsync();

        var capabilitiesQuery = dbContext.TrmCapabilities.AsNoTracking().OrderBy(x => x.Code).AsQueryable();
        if (postedModel.SelectedDomainId.HasValue)
        {
            capabilitiesQuery = capabilitiesQuery.Where(x => x.ParentDomainId == postedModel.SelectedDomainId);
        }

        var capabilities = await capabilitiesQuery
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
            .ToListAsync();

        var componentsQuery = dbContext.TrmComponents
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code)
            .AsQueryable();
        if (postedModel.SelectedCapabilityId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.CapabilityLinks.Any(link => link.TrmCapabilityId == postedModel.SelectedCapabilityId));
        }

        var components = await componentsQuery
            .OrderBy(x => x.IsCustom)
            .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
            .Select(x => new SelectListItem((x.IsCustom ? (x.TechnologyComponentCode ?? x.Code) : x.Code) + " " + x.Name, x.Id.ToString()))
            .ToListAsync();

        return new MappingEditViewModel
        {
            MappingId = mappingIdOverride ?? postedModel.MappingId,
            ProductId = product.Id,
            ProductName = product.Name,
            Vendor = product.Vendor,
            Version = product.Version,
            Description = product.Description,
            LifecycleStatus = product.LifecycleStatus,
            Owners = postedModel.Owners,
            SelectedDomainId = postedModel.SelectedDomainId,
            SelectedCapabilityId = postedModel.SelectedCapabilityId,
            SelectedComponentId = postedModel.SelectedComponentId,
            MappingStatus = postedModel.MappingStatus,
            CustomTechnologyComponentCode = postedModel.CustomTechnologyComponentCode,
            CustomComponentName = postedModel.CustomComponentName,
            MappingRationale = postedModel.MappingRationale,
            Domains = domains,
            Capabilities = capabilities,
            Components = components,
            OwnerOptions = await configurableFieldService.GetMultiSelectListAsync(ConfigurableFieldNames.Owner, postedModel.Owners)
        };
    }

    private async Task<(TrmComponent? Component, string? ErrorMessage, bool WasCreated, bool WasChanged)> ResolveOrCreateCustomComponentAsync(
        TrmCapability capability,
        string technologyComponentCode,
        string componentName)
    {
        var modelCodeExists = await dbContext.TrmComponents.AnyAsync(x =>
            !x.IsCustom && x.Code.ToLower() == technologyComponentCode.ToLower());

        if (modelCodeExists)
        {
            return (null, "That Technology Component Code already exists in the imported TRM model. Choose the model component instead.", false, false);
        }

        var existingCustomComponent = await dbContext.TrmComponents
            .Include(x => x.CapabilityLinks)
            .FirstOrDefaultAsync(x =>
                x.IsCustom &&
                !x.IsDeleted &&
                x.TechnologyComponentCode != null &&
                x.TechnologyComponentCode.ToLower() == technologyComponentCode.ToLower());

        if (existingCustomComponent is not null)
        {
            var wasChanged = existingCustomComponent.Name != componentName;
            existingCustomComponent.Name = componentName;

            if (!existingCustomComponent.CapabilityLinks.Any(x => x.TrmCapabilityId == capability.Id))
            {
                existingCustomComponent.CapabilityLinks.Add(new TrmComponentCapabilityLink
                {
                    TrmCapabilityId = capability.Id,
                    CreatedUtc = DateTime.UtcNow
                });
                wasChanged = true;
            }

            if (existingCustomComponent.ParentCapabilityId is null)
            {
                existingCustomComponent.ParentCapabilityId = capability.Id;
                existingCustomComponent.ParentCapabilityCode = capability.Code;
                wasChanged = true;
            }

            return (existingCustomComponent, null, false, wasChanged);
        }

        var component = new TrmComponent
        {
            Code = GenerateInternalCustomComponentCode(),
            TechnologyComponentCode = technologyComponentCode,
            Name = componentName,
            SourceTitle = "Custom technology component",
            ParentCapabilityCode = capability.Code,
            ParentCapabilityId = capability.Id,
            IsCustom = true
        };

        component.CapabilityLinks.Add(new TrmComponentCapabilityLink
        {
            TrmCapabilityId = capability.Id,
            CreatedUtc = DateTime.UtcNow
        });

        dbContext.TrmComponents.Add(component);
        return (component, null, true, true);
    }

    private static string GenerateInternalCustomComponentCode() =>
        $"CUS{Guid.NewGuid():N}"[..11].ToUpperInvariant();

    private static string? NormalizeInput(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SynchronizeOwners(ProductCatalogItem product, IReadOnlyCollection<string> selectedOwners)
    {
        var existingOwners = product.Owners.ToList();

        foreach (var owner in existingOwners.Where(owner =>
                     selectedOwners.All(selected => !string.Equals(selected, owner.OwnerValue, StringComparison.OrdinalIgnoreCase))))
        {
            product.Owners.Remove(owner);
            dbContext.ProductCatalogItemOwners.Remove(owner);
        }

        foreach (var owner in selectedOwners.Where(selected =>
                     existingOwners.All(existing => !string.Equals(existing.OwnerValue, selected, StringComparison.OrdinalIgnoreCase))))
        {
            product.Owners.Add(new ProductCatalogItemOwner
            {
                OwnerValue = owner
            });
        }
    }

    private static List<string> NormalizeSelections(IEnumerable<string>? values)
    {
        var normalized = new List<string>();
        if (values is null)
        {
            return normalized;
        }

        foreach (var value in values)
        {
            var trimmed = NormalizeInput(value);
            if (trimmed is null || normalized.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized;
    }

    private async Task WriteMappingAuditAsync(ProductMapping mapping, string action, string details)
    {
        await auditLogService.WriteAsync(
            "Mapping",
            action,
            nameof(ProductMapping),
            mapping.Id,
            $"{action}d mapping for {mapping.ProductCatalogItem?.Name ?? "product"}.",
            details);
    }

    private async Task WriteCustomComponentHistoryAsync(MappingUpdateResult updateResult, TrmComponent? component)
    {
        if (component is null || !component.IsCustom || !updateResult.CustomComponentChanged)
        {
            return;
        }

        await componentVersioningService.RecordVersionAsync(
            component.Id,
            updateResult.CustomComponentCreated ? "Created" : "Updated",
            "Custom component maintained from mapping.");

        await auditLogService.WriteAsync(
            "Component",
            updateResult.CustomComponentCreated ? "Create" : "Update",
            nameof(TrmComponent),
            component.Id,
            $"{(updateResult.CustomComponentCreated ? "Created" : "Updated")} custom component {component.DisplayLabel}.",
            "Triggered from mapping edit.");
    }

    private sealed record MappingUpdateResult(bool CustomComponentCreated = false, bool CustomComponentChanged = false);
}
