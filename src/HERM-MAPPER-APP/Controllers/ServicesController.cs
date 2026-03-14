using System.Globalization;
using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class ServicesController(
    AppDbContext dbContext,
    AuditLogService auditLogService,
    ConfigurableFieldService configurableFieldService) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        string? owner = null,
        string? lifecycleStatus = null,
        string? sort = null)
    {
        owner = NormalizeSelection(owner);
        lifecycleStatus = NormalizeSelection(lifecycleStatus);
        sort = NormalizeSort(sort);
        var caseInsensitiveCollation = AppDatabaseCollations.GetCaseInsensitive(dbContext.Database);

        var query = dbContext.ServiceCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .AsSplitQuery()
            .AsQueryable();

        var likePattern = SearchPattern.CreateContainsPattern(search);
        if (likePattern is not null)
        {
            query = query.Where(x =>
                EF.Functions.Like(x.Name, likePattern) ||
                (x.Description != null && EF.Functions.Like(x.Description, likePattern)) ||
                EF.Functions.Like(x.Owner, likePattern) ||
                EF.Functions.Like(x.LifecycleStatus, likePattern) ||
                x.ProductLinks.Any(link => EF.Functions.Like(link.ProductCatalogItem.Name, likePattern)));
        }

        if (owner is not null)
        {
            query = query.Where(x => EF.Functions.Collate(x.Owner, caseInsensitiveCollation) == owner);
        }

        if (lifecycleStatus is not null)
        {
            query = query.Where(x => EF.Functions.Collate(x.LifecycleStatus, caseInsensitiveCollation) == lifecycleStatus);
        }

        query = ApplySort(query, sort);

        var services = await query.ToListAsync();

        return View(new ServicesIndexViewModel
        {
            Search = search,
            Owner = owner,
            LifecycleStatus = lifecycleStatus,
            Sort = sort,
            AvailableOwners = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.Owner),
            LifecycleStatuses = await configurableFieldService.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus),
            Services = services.Select(BuildIndexRow).ToList()
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Create()
    {
        var model = new ServiceEditViewModel();
        EnsureEditableRows(model);
        await PopulateFormOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceEditViewModel input)
    {
        NormalizeFormInput(input);
        await ValidateSelectedProductsAsync(input, Array.Empty<int>());

        if (!ModelState.IsValid)
        {
            EnsureEditableRows(input);
            await PopulateFormOptionsAsync(input);
            return View(input);
        }

        var service = new ServiceCatalogItem
        {
            Name = input.Name,
            Description = NormalizeSelection(input.Description),
            Owner = input.Owner!,
            LifecycleStatus = input.LifecycleStatus!,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        SynchronizeProductLinks(service, input.ProductRows);

        dbContext.ServiceCatalogItems.Add(service);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Create",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Created service {service.Name}.",
            $"Owner: {service.Owner}. Status: {service.LifecycleStatus}. Connections: {service.ConnectionCount}.");

        TempData["ServicesStatusMessage"] = $"Created service {service.Name}.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Edit(int id)
    {
        var service = await LoadServiceAsync(id, asNoTracking: true);
        if (service is null)
        {
            return NotFound();
        }

        var model = ServiceEditViewModel.FromService(service);
        EnsureEditableRows(model);
        await PopulateFormOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceEditViewModel input)
    {
        var service = await LoadServiceAsync(id, asNoTracking: false);
        if (service is null)
        {
            return NotFound();
        }

        input.Id = id;
        NormalizeFormInput(input);
        var allowedDeletedProductIds = service.ProductLinks
            .Select(x => x.ProductCatalogItemId)
            .Distinct()
            .ToList();
        await ValidateSelectedProductsAsync(input, allowedDeletedProductIds);

        if (!ModelState.IsValid)
        {
            EnsureEditableRows(input);
            await PopulateFormOptionsAsync(input);
            return View(input);
        }

        service.Name = input.Name;
        service.Description = NormalizeSelection(input.Description);
        service.Owner = input.Owner!;
        service.LifecycleStatus = input.LifecycleStatus!;
        service.UpdatedUtc = DateTime.UtcNow;

        SynchronizeProductLinks(service, input.ProductRows);

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Update",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Updated service {service.Name}.",
            $"Owner: {service.Owner}. Status: {service.LifecycleStatus}. Connections: {service.ConnectionCount}.");

        TempData["ServicesStatusMessage"] = $"Updated service {service.Name}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Visualize(int id)
    {
        var service = await LoadServiceAsync(id, asNoTracking: true);
        if (service is null)
        {
            return NotFound();
        }

        var orderedProductNames = service.GetOrderedProductLinks()
            .Select(x => x.ProductCatalogItem.Name)
            .ToList();

        return View(new ServiceVisualizationViewModel
        {
            Service = service,
            ProductNames = orderedProductNames,
            Connections = BuildConnections(service.Name, orderedProductNames)
        });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var service = await LoadServiceAsync(id, asNoTracking: true);
        return service is null ? NotFound() : View(service);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost, ActionName(nameof(Delete))]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var service = await dbContext.ServiceCatalogItems.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (service is null)
        {
            return NotFound();
        }

        service.IsDeleted = true;
        service.DeletedUtc = DateTime.UtcNow;
        service.DeletedReason = "Moved to trash from the service catalogue.";
        service.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Delete",
            nameof(ServiceCatalogItem),
            id,
            $"Moved service {service.Name} to trash.",
            service.DeletedReason);

        TempData["ServicesStatusMessage"] = $"Moved service {service.Name} to trash.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    public async Task<IActionResult> Restore()
    {
        var services = await dbContext.ServiceCatalogItems
            .AsNoTracking()
            .Include(x => x.ProductLinks)
            .ThenInclude(x => x.ProductCatalogItem)
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedUtc)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return View(new ServiceRestoreViewModel
        {
            Services = services,
            StatusMessage = TempData["ServicesStatusMessage"] as string
        });
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreDeleted(int id)
    {
        var service = await dbContext.ServiceCatalogItems.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (service is null)
        {
            return NotFound();
        }

        service.IsDeleted = false;
        service.DeletedUtc = null;
        service.DeletedReason = null;
        service.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Restore",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Restored service {service.Name} from trash.");

        TempData["ServicesStatusMessage"] = $"Restored service {service.Name}.";
        return RedirectToAction(nameof(Restore));
    }

    [Authorize(Policy = AppPolicies.AdminOnly)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDelete(int id)
    {
        var service = await dbContext.ServiceCatalogItems.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (service is null)
        {
            return NotFound();
        }

        dbContext.ServiceCatalogItems.Remove(service);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "PermanentDelete",
            nameof(ServiceCatalogItem),
            id,
            $"Permanently deleted service {service.Name}.");

        TempData["ServicesStatusMessage"] = $"Permanently deleted service {service.Name}.";
        return RedirectToAction(nameof(Restore));
    }

    private async Task PopulateFormOptionsAsync(ServiceEditViewModel model)
    {
        model.OwnerOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.Owner,
            model.Owner,
            "Choose owner");
        model.LifecycleStatusOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            model.LifecycleStatus,
            "Choose status");
        model.ProductOptions = await BuildProductOptionsAsync(model.ProductRows.Select(x => x.ProductId));
    }

    private async Task<List<SelectListItem>> BuildProductOptionsAsync(IEnumerable<int?> selectedProductIds)
    {
        var selectedIds = selectedProductIds
            .Where(x => x is > 0)
            .Select(x => x!.Value)
            .ToHashSet();

        var products = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Where(x => !x.IsDeleted || selectedIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Vendor)
            .ThenBy(x => x.Version)
            .ToListAsync();

        var items = new List<SelectListItem>
        {
            new("Choose product", string.Empty, selected: false)
        };

        items.AddRange(products.Select(product =>
            new SelectListItem(
                BuildProductLabel(product),
                product.Id.ToString(CultureInfo.InvariantCulture),
                selected: false)));

        return items;
    }

    private static string BuildProductLabel(ProductCatalogItem product)
    {
        var detailParts = new[] { product.Vendor, product.Version }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return detailParts.Count == 0
            ? BuildDeletedProductLabel(product.Name, product.IsDeleted)
            : BuildDeletedProductLabel($"{product.Name} ({string.Join(" ", detailParts)})", product.IsDeleted);
    }

    private async Task<ServiceCatalogItem?> LoadServiceAsync(int id, bool asNoTracking)
    {
        IQueryable<ServiceCatalogItem> query = dbContext.ServiceCatalogItems
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .ThenInclude(x => x.ProductCatalogItem)
            .Where(x => !x.IsDeleted)
            .AsSplitQuery();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(x => x.Id == id);
    }

    private static ServiceIndexRowViewModel BuildIndexRow(ServiceCatalogItem service)
    {
        var productNames = service.GetOrderedProductLinks()
            .Select(x => x.ProductCatalogItem.Name)
            .ToList();

        return new ServiceIndexRowViewModel
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Owner = service.Owner,
            LifecycleStatus = service.LifecycleStatus,
            UpdatedUtc = service.UpdatedUtc,
            ProductNames = productNames
        };
    }

    private async Task ValidateSelectedProductsAsync(ServiceEditViewModel input, IReadOnlyCollection<int> allowedDeletedProductIds)
    {
        var selectedProductIds = input.ProductRows
            .Where(x => x.ProductId is > 0)
            .Select(x => x.ProductId!.Value)
            .Distinct()
            .ToList();

        if (selectedProductIds.Count == 0)
        {
            return;
        }

        var existingProductIds = await dbContext.ProductCatalogItems
            .AsNoTracking()
            .Where(x => selectedProductIds.Contains(x.Id) && (!x.IsDeleted || allowedDeletedProductIds.Contains(x.Id)))
            .Select(x => x.Id)
            .ToListAsync();

        if (existingProductIds.Count != selectedProductIds.Count)
        {
            ModelState.AddModelError(nameof(input.ProductRows), "One or more selected products could not be found.");
        }
    }

    private void SynchronizeProductLinks(ServiceCatalogItem service, List<ServiceProductRowViewModel> rows)
    {
        var existingLinks = service.ProductLinks
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        for (var index = 0; index < rows.Count; index++)
        {
            var productId = rows[index].ProductId;
            if (productId is not > 0)
            {
                continue;
            }

            if (index < existingLinks.Count)
            {
                existingLinks[index].ProductCatalogItemId = productId.Value;
                existingLinks[index].SortOrder = index + 1;
            }
            else
            {
                service.ProductLinks.Add(new ServiceCatalogItemProduct
                {
                    ProductCatalogItemId = productId.Value,
                    SortOrder = index + 1
                });
            }
        }

        foreach (var existingLink in existingLinks.Skip(rows.Count))
        {
            service.ProductLinks.Remove(existingLink);
            dbContext.ServiceCatalogItemProducts.Remove(existingLink);
        }
    }

    private static List<ServiceConnectionViewModel> BuildConnections(string serviceName, List<string> productNames)
    {
        var connections = new List<ServiceConnectionViewModel>();

        for (var index = 0; index < productNames.Count - 1; index++)
        {
            connections.Add(new ServiceConnectionViewModel
            {
                Sequence = index + 1,
                FromProductName = productNames[index],
                ToProductName = productNames[index + 1],
                ServiceName = serviceName
            });
        }

        return connections;
    }

    private static string BuildDeletedProductLabel(string label, bool isDeleted) =>
        isDeleted ? $"{label} [deleted]" : label;

    private static IQueryable<ServiceCatalogItem> ApplySort(IQueryable<ServiceCatalogItem> query, string sort) =>
        sort switch
        {
            ServiceSortOptions.Name => query.OrderBy(x => x.Name).ThenByDescending(x => x.UpdatedUtc),
            ServiceSortOptions.NameDesc => query.OrderByDescending(x => x.Name).ThenByDescending(x => x.UpdatedUtc),
            ServiceSortOptions.Owner => query.OrderBy(x => x.Owner).ThenBy(x => x.Name),
            ServiceSortOptions.Lifecycle => query.OrderBy(x => x.LifecycleStatus).ThenBy(x => x.Name),
            ServiceSortOptions.ProductCountDesc => query.OrderByDescending(x => x.ProductLinks.Count).ThenBy(x => x.Name),
            ServiceSortOptions.UpdatedAsc => query.OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Name),
            _ => query.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Name)
        };

    private static void NormalizeFormInput(ServiceEditViewModel input)
    {
        input.Owner = NormalizeSelection(input.Owner);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);
        input.ProductRows = NormalizeProductRows(input.ProductRows);
    }

    private static List<ServiceProductRowViewModel> NormalizeProductRows(IEnumerable<ServiceProductRowViewModel>? rows)
    {
        var normalized = new List<ServiceProductRowViewModel>();
        if (rows is null)
        {
            return normalized;
        }

        foreach (var row in rows)
        {
            if (row.ProductId is not > 0)
            {
                continue;
            }

            normalized.Add(new ServiceProductRowViewModel
            {
                ProductId = row.ProductId
            });
        }

        return normalized;
    }

    private static string? NormalizeSelection(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeSort(string? value) =>
        value switch
        {
            ServiceSortOptions.Name => ServiceSortOptions.Name,
            ServiceSortOptions.NameDesc => ServiceSortOptions.NameDesc,
            ServiceSortOptions.Owner => ServiceSortOptions.Owner,
            ServiceSortOptions.Lifecycle => ServiceSortOptions.Lifecycle,
            ServiceSortOptions.ProductCountDesc => ServiceSortOptions.ProductCountDesc,
            ServiceSortOptions.UpdatedAsc => ServiceSortOptions.UpdatedAsc,
            _ => ServiceSortOptions.UpdatedDesc
        };

    private static void EnsureEditableRows(ServiceEditViewModel model)
    {
        while (model.ProductRows.Count < 2)
        {
            model.ProductRows.Add(new ServiceProductRowViewModel());
        }
    }
}
