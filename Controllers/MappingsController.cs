using System.Text;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class MappingsController(AppDbContext dbContext, CsvExportService csvExportService) : Controller
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
                x.Mappings.Any(m => m.TrmComponent != null && m.TrmComponent.Name.Contains(search)));
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
        var product = await dbContext.ProductCatalogItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId);
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
        var product = await dbContext.ProductCatalogItems.FindAsync(model.ProductId);
        if (product is null)
        {
            return NotFound();
        }

        var mapping = new ProductMapping
        {
            ProductCatalogItemId = product.Id,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        if (!await PopulateAndValidateMapping(model, mapping))
        {
            return View("Edit", await BuildMappingEditViewModel(product, model));
        }

        dbContext.ProductMappings.Add(mapping);
        product.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var mapping = await dbContext.ProductMappings
            .AsNoTracking()
            .Include(x => x.ProductCatalogItem)
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
            .FirstOrDefaultAsync(x => x.Id == id);

        if (mapping?.ProductCatalogItem is null)
        {
            return NotFound();
        }

        if (!await PopulateAndValidateMapping(model, mapping))
        {
            return View(await BuildMappingEditViewModel(mapping.ProductCatalogItem, model, mapping.Id));
        }

        mapping.UpdatedUtc = DateTime.UtcNow;
        mapping.ProductCatalogItem.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

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
            query = query.Where(x => x.ParentCapabilityId == capabilityId);
        }

        var components = await query
            .OrderBy(x => x.Code)
            .Select(x => new { id = x.Id, text = x.Code + " " + x.Name })
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
                (x.TrmComponent != null && x.TrmComponent.Name.Contains(search)));
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

    private async Task<bool> PopulateAndValidateMapping(MappingEditViewModel model, ProductMapping mapping)
    {
        TrmDomain? domain = null;
        TrmCapability? capability = null;
        TrmComponent? component = null;

        if (model.SelectedComponentId.HasValue)
        {
            component = await dbContext.TrmComponents
                .Include(x => x.ParentCapability)
                .ThenInclude(x => x!.ParentDomain)
                .FirstOrDefaultAsync(x => x.Id == model.SelectedComponentId.Value);

            if (component is null)
            {
                ModelState.AddModelError(nameof(model.SelectedComponentId), "Choose a valid HERM TRM component.");
            }
            else
            {
                capability = component.ParentCapability;
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
            return false;
        }

        mapping.TrmDomainId = domain?.Id;
        mapping.TrmCapabilityId = capability?.Id;
        mapping.TrmComponentId = component?.Id;
        mapping.MappingStatus = model.MappingStatus;
        mapping.FitScore = model.FitScore;
        mapping.MappingRationale = model.MappingRationale;
        mapping.LastReviewedUtc = DateTime.UtcNow;
        return true;
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

        var componentsQuery = dbContext.TrmComponents.AsNoTracking().OrderBy(x => x.Code).AsQueryable();
        if (selectedCapabilityId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.ParentCapabilityId == selectedCapabilityId);
        }

        var components = await componentsQuery
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
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
            Owner = product.Owner,
            SelectedDomainId = selectedDomainId,
            SelectedCapabilityId = selectedCapabilityId,
            SelectedComponentId = selectedComponentId,
            MappingStatus = mapping?.MappingStatus ?? MappingStatus.Draft,
            FitScore = mapping?.FitScore,
            MappingRationale = mapping?.MappingRationale,
            Domains = domains,
            Capabilities = capabilities,
            Components = components
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

        var componentsQuery = dbContext.TrmComponents.AsNoTracking().OrderBy(x => x.Code).AsQueryable();
        if (postedModel.SelectedCapabilityId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.ParentCapabilityId == postedModel.SelectedCapabilityId);
        }

        var components = await componentsQuery
            .Select(x => new SelectListItem($"{x.Code} {x.Name}", x.Id.ToString()))
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
            Owner = product.Owner,
            SelectedDomainId = postedModel.SelectedDomainId,
            SelectedCapabilityId = postedModel.SelectedCapabilityId,
            SelectedComponentId = postedModel.SelectedComponentId,
            MappingStatus = postedModel.MappingStatus,
            FitScore = postedModel.FitScore,
            MappingRationale = postedModel.MappingRationale,
            Domains = domains,
            Capabilities = capabilities,
            Components = components
        };
    }
}
