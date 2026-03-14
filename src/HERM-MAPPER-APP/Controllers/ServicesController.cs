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
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.FromProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.ToProductCatalogItem)
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
                x.ProductLinks.Any(link => EF.Functions.Like(link.ProductCatalogItem.Name, likePattern)) ||
                x.ProductConnections.Any(connection =>
                    EF.Functions.Like(connection.FromProductCatalogItem.Name, likePattern) ||
                    EF.Functions.Like(connection.ToProductCatalogItem.Name, likePattern)));
        }

        if (owner is not null)
        {
            query = query.Where(x => EF.Functions.Collate(x.Owner, caseInsensitiveCollation) == owner);
        }

        if (lifecycleStatus is not null)
        {
            query = query.Where(x => EF.Functions.Collate(x.LifecycleStatus, caseInsensitiveCollation) == lifecycleStatus);
        }

        var services = ApplySort(await query.ToListAsync(), sort).ToList();

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
        await PopulateServiceOptionsAsync(model);
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceEditViewModel input)
    {
        NormalizeServiceInput(input);

        if (!ModelState.IsValid)
        {
            await PopulateServiceOptionsAsync(input);
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

        dbContext.ServiceCatalogItems.Add(service);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Create",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Created service {service.Name}.",
            $"Owner: {service.Owner}. Status: {service.LifecycleStatus}. Connections: 0.");

        TempData["ServicesStatusMessage"] = $"Created service {service.Name}. Design the connected products below.";
        return RedirectToAction(nameof(Connections), new { id = service.Id });
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
        await PopulateServiceOptionsAsync(model);
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
        NormalizeServiceInput(input);

        if (!ModelState.IsValid)
        {
            await PopulateServiceOptionsAsync(input);
            return View(input);
        }

        service.Name = input.Name;
        service.Description = NormalizeSelection(input.Description);
        service.Owner = input.Owner!;
        service.LifecycleStatus = input.LifecycleStatus!;
        service.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "Update",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Updated service {service.Name}.",
            $"Owner: {service.Owner}. Status: {service.LifecycleStatus}. Connections: {service.ConnectionCount}.");

        TempData["ServicesStatusMessage"] = $"Updated service {service.Name}.";
        return RedirectToAction(nameof(Connections), new { id = service.Id });
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    public async Task<IActionResult> Connections(int id)
    {
        var service = await LoadServiceAsync(id, asNoTracking: true);
        if (service is null)
        {
            return NotFound();
        }

        var model = await BuildConnectionEditorModelAsync(service);
        model.StatusMessage = TempData["ServicesStatusMessage"] as string;
        return View(model);
    }

    [Authorize(Policy = AppPolicies.ProductsAndServicesWrite)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connections(int id, ServiceConnectionEditorViewModel input)
    {
        var service = await LoadServiceAsync(id, asNoTracking: false);
        if (service is null)
        {
            return NotFound();
        }

        input.ServiceId = id;
        HydrateConnectionEditorSummary(input, service);
        NormalizeConnectionInput(input);
        var allowedDeletedProductIds = GetAllowedDeletedProductIds(service);
        await ValidateConnectionRowsAsync(input, allowedDeletedProductIds);

        if (!ModelState.IsValid)
        {
            EnsureConnectionRows(input);
            await PopulateConnectionEditorOptionsAsync(input, allowedDeletedProductIds);
            return View(input);
        }

        SynchronizeProductConnections(service, input.ConnectionRows);
        SynchronizeLegacyProductLinksFromConnections(service, input.ConnectionRows);
        service.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Service",
            "UpdateConnections",
            nameof(ServiceCatalogItem),
            service.Id,
            $"Updated connection designer for service {service.Name}.",
            $"Connections: {service.ConnectionCount}. Products: {service.ProductLinks.Count}.");

        TempData["ServicesStatusMessage"] = $"Saved connected products for {service.Name}.";
        return RedirectToAction(nameof(Connections), new { id = service.Id });
    }

    public async Task<IActionResult> Visualize(int id)
    {
        var service = await LoadServiceAsync(id, asNoTracking: true);
        if (service is null)
        {
            return NotFound();
        }

        var usesGraphConnections = service.ProductConnections.Count != 0;
        var productNames = GetOrderedProductLabels(service);
        var connections = usesGraphConnections
            ? BuildGraphConnections(service)
            : BuildLegacyConnections(service);

        return View(new ServiceVisualizationViewModel
        {
            Service = service,
            ProductNames = productNames,
            Connections = connections,
            UsesGraphConnections = usesGraphConnections,
            SupportsGraphLayout = usesGraphConnections && CanRenderAsGraph(connections)
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
            .Include(x => x.ProductLinks.OrderBy(link => link.SortOrder))
            .ThenInclude(x => x.ProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.FromProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.ToProductCatalogItem)
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

    private async Task PopulateServiceOptionsAsync(ServiceEditViewModel model)
    {
        model.OwnerOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.Owner,
            model.Owner,
            "Choose owner");
        model.LifecycleStatusOptions = await configurableFieldService.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            model.LifecycleStatus,
            "Choose status");
    }

    private async Task<ServiceConnectionEditorViewModel> BuildConnectionEditorModelAsync(ServiceCatalogItem service)
    {
        var model = new ServiceConnectionEditorViewModel
        {
            ServiceId = service.Id,
            ServiceName = service.Name,
            ServiceDescription = service.Description,
            ServiceOwner = service.Owner,
            ServiceLifecycleStatus = service.LifecycleStatus,
            UsesLegacyFlow = service.ProductConnections.Count == 0 && service.ProductLinks.Count > 1,
            ConnectionRows = BuildConnectionRowsForEditor(service)
        };

        EnsureConnectionRows(model);
        await PopulateConnectionEditorOptionsAsync(model, GetAllowedDeletedProductIds(service));
        return model;
    }

    private async Task PopulateConnectionEditorOptionsAsync(
        ServiceConnectionEditorViewModel model,
        IReadOnlyCollection<int> allowedDeletedProductIds)
    {
        model.ProductOptions = await BuildProductOptionsAsync(allowedDeletedProductIds);
    }

    private async Task<List<SelectListItem>> BuildProductOptionsAsync(IEnumerable<int> selectedProductIds)
    {
        var selectedIds = selectedProductIds
            .Where(x => x > 0)
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
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.FromProductCatalogItem)
            .Include(x => x.ProductConnections.OrderBy(connection => connection.SortOrder))
            .ThenInclude(x => x.ToProductCatalogItem)
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
        var productNames = GetOrderedProductLabels(service);

        return new ServiceIndexRowViewModel
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Owner = service.Owner,
            LifecycleStatus = service.LifecycleStatus,
            UpdatedUtc = service.UpdatedUtc,
            ProductNames = productNames,
            ProductCount = productNames.Count,
            ConnectionCount = service.ConnectionCount,
            ProductPreview = BuildProductPreview(productNames)
        };
    }

    private async Task ValidateConnectionRowsAsync(
        ServiceConnectionEditorViewModel input,
        IReadOnlyCollection<int> allowedDeletedProductIds)
    {
        var rowsWithSelections = input.ConnectionRows
            .Where(row => row.FromProductId is > 0 || row.ToProductId is > 0)
            .ToList();

        if (rowsWithSelections.Any(row => row.FromProductId is not > 0 || row.ToProductId is not > 0))
        {
            ModelState.AddModelError(nameof(input.ConnectionRows), "Choose both products for every connection row.");
        }

        var completedRows = rowsWithSelections
            .Where(row => row.FromProductId is > 0 && row.ToProductId is > 0)
            .ToList();

        if (completedRows.Any(row => row.FromProductId == row.ToProductId))
        {
            ModelState.AddModelError(nameof(input.ConnectionRows), "A connection cannot point back to the same product in the same row.");
        }

        var duplicateEdges = completedRows
            .GroupBy(row => new { row.FromProductId, row.ToProductId })
            .Where(group => group.Count() > 1)
            .ToList();
        if (duplicateEdges.Count != 0)
        {
            ModelState.AddModelError(nameof(input.ConnectionRows), "Each product-to-product connection should only be listed once.");
        }

        var selectedProductIds = completedRows
            .SelectMany(row => new[] { row.FromProductId!.Value, row.ToProductId!.Value })
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
            ModelState.AddModelError(nameof(input.ConnectionRows), "One or more selected products could not be found.");
        }
    }

    private void SynchronizeProductConnections(ServiceCatalogItem service, List<ServiceConnectionRowInputViewModel> rows)
    {
        var completedRows = rows
            .Where(row => row.FromProductId is > 0 && row.ToProductId is > 0)
            .ToList();

        var existingConnections = service.ProductConnections
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        for (var index = 0; index < completedRows.Count; index++)
        {
            var row = completedRows[index];

            if (index < existingConnections.Count)
            {
                existingConnections[index].FromProductCatalogItemId = row.FromProductId!.Value;
                existingConnections[index].ToProductCatalogItemId = row.ToProductId!.Value;
                existingConnections[index].SortOrder = index + 1;
            }
            else
            {
                service.ProductConnections.Add(new ServiceCatalogItemConnection
                {
                    FromProductCatalogItemId = row.FromProductId!.Value,
                    ToProductCatalogItemId = row.ToProductId!.Value,
                    SortOrder = index + 1
                });
            }
        }

        foreach (var existingConnection in existingConnections.Skip(completedRows.Count))
        {
            service.ProductConnections.Remove(existingConnection);
            dbContext.ServiceCatalogItemConnections.Remove(existingConnection);
        }
    }

    private void SynchronizeLegacyProductLinksFromConnections(
        ServiceCatalogItem service,
        IEnumerable<ServiceConnectionRowInputViewModel> rows)
    {
        var orderedProductIds = BuildOrderedGraphProductIds(
            rows
                .Where(row => row.FromProductId is > 0 && row.ToProductId is > 0)
                .Select(row => new ConnectionPair(row.FromProductId!.Value, row.ToProductId!.Value))
                .ToList(),
            out _);

        var existingLinks = service.ProductLinks
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        for (var index = 0; index < orderedProductIds.Count; index++)
        {
            if (index < existingLinks.Count)
            {
                existingLinks[index].ProductCatalogItemId = orderedProductIds[index];
                existingLinks[index].SortOrder = index + 1;
            }
            else
            {
                service.ProductLinks.Add(new ServiceCatalogItemProduct
                {
                    ProductCatalogItemId = orderedProductIds[index],
                    SortOrder = index + 1
                });
            }
        }

        foreach (var existingLink in existingLinks.Skip(orderedProductIds.Count))
        {
            service.ProductLinks.Remove(existingLink);
            dbContext.ServiceCatalogItemProducts.Remove(existingLink);
        }
    }

    private static List<ServiceConnectionRowInputViewModel> BuildConnectionRowsForEditor(ServiceCatalogItem service)
    {
        if (service.ProductConnections.Count != 0)
        {
            return service.GetOrderedProductConnections()
                .Select(connection => new ServiceConnectionRowInputViewModel
                {
                    FromProductId = connection.FromProductCatalogItemId,
                    ToProductId = connection.ToProductCatalogItemId
                })
                .ToList();
        }

        var links = service.GetOrderedProductLinks();
        var rows = new List<ServiceConnectionRowInputViewModel>();

        for (var index = 0; index < links.Count - 1; index++)
        {
            rows.Add(new ServiceConnectionRowInputViewModel
            {
                FromProductId = links[index].ProductCatalogItemId,
                ToProductId = links[index + 1].ProductCatalogItemId
            });
        }

        return rows;
    }

    private static List<ServiceConnectionViewModel> BuildGraphConnections(ServiceCatalogItem service) => service
        .GetOrderedProductConnections()
        .Select((connection, index) => new ServiceConnectionViewModel
        {
            Sequence = index + 1,
            FromProductId = connection.FromProductCatalogItemId,
            ToProductId = connection.ToProductCatalogItemId,
            FromProductName = BuildProductLabel(connection.FromProductCatalogItem),
            ToProductName = BuildProductLabel(connection.ToProductCatalogItem),
            ServiceName = service.Name
        })
        .ToList();

    private static List<ServiceConnectionViewModel> BuildLegacyConnections(ServiceCatalogItem service)
    {
        var orderedLinks = service.GetOrderedProductLinks();
        var connections = new List<ServiceConnectionViewModel>();

        for (var index = 0; index < orderedLinks.Count - 1; index++)
        {
            connections.Add(new ServiceConnectionViewModel
            {
                Sequence = index + 1,
                FromProductId = orderedLinks[index].ProductCatalogItemId,
                ToProductId = orderedLinks[index + 1].ProductCatalogItemId,
                FromProductName = BuildProductLabel(orderedLinks[index].ProductCatalogItem),
                ToProductName = BuildProductLabel(orderedLinks[index + 1].ProductCatalogItem),
                ServiceName = service.Name
            });
        }

        return connections;
    }

    private static List<string> GetOrderedProductLabels(ServiceCatalogItem service)
    {
        if (service.ProductConnections.Count == 0)
        {
            return service.GetOrderedProductLinks()
                .Select(link => BuildProductLabel(link.ProductCatalogItem))
                .ToList();
        }

        var orderedConnections = service.GetOrderedProductConnections();
        var orderedProductIds = BuildOrderedGraphProductIds(
            orderedConnections
                .Select(connection => new ConnectionPair(connection.FromProductCatalogItemId, connection.ToProductCatalogItemId))
                .ToList(),
            out _);

        var labelsById = new Dictionary<int, string>();
        foreach (var connection in orderedConnections)
        {
            labelsById.TryAdd(connection.FromProductCatalogItemId, BuildProductLabel(connection.FromProductCatalogItem));
            labelsById.TryAdd(connection.ToProductCatalogItemId, BuildProductLabel(connection.ToProductCatalogItem));
        }

        return orderedProductIds
            .Where(labelsById.ContainsKey)
            .Select(id => labelsById[id])
            .ToList();
    }

    private static string BuildProductPreview(IReadOnlyList<string> productNames) => productNames.Count switch
    {
        0 => "-",
        <= 3 => string.Join(", ", productNames),
        _ => $"{string.Join(", ", productNames.Take(3))} +{productNames.Count - 3} more"
    };

    private static IReadOnlyCollection<int> GetAllowedDeletedProductIds(ServiceCatalogItem service) => service.ProductLinks
        .Select(link => link.ProductCatalogItemId)
        .Concat(service.ProductConnections.SelectMany(connection => new[]
        {
            connection.FromProductCatalogItemId,
            connection.ToProductCatalogItemId
        }))
        .Distinct()
        .ToList();

    private static bool CanRenderAsGraph(IReadOnlyList<ServiceConnectionViewModel> connections)
    {
        if (connections.Count == 0)
        {
            return false;
        }

        return BuildOrderedGraphProductIds(
                connections.Select(connection => new ConnectionPair(connection.FromProductId, connection.ToProductId)).ToList(),
                out var supportsGraphLayout)
            .Count != 0 && supportsGraphLayout;
    }

    private static List<int> BuildOrderedGraphProductIds(
        IReadOnlyList<ConnectionPair> connections,
        out bool supportsGraphLayout)
    {
        supportsGraphLayout = false;
        if (connections.Count == 0)
        {
            return [];
        }

        var firstAppearance = new Dictionary<int, int>();
        var adjacency = new Dictionary<int, HashSet<int>>();
        var indegree = new Dictionary<int, int>();
        var appearanceIndex = 0;

        static void EnsureNode(
            int productId,
            Dictionary<int, int> firstAppearance,
            Dictionary<int, HashSet<int>> adjacency,
            Dictionary<int, int> indegree,
            ref int appearanceIndex)
        {
            if (!firstAppearance.ContainsKey(productId))
            {
                firstAppearance[productId] = appearanceIndex++;
            }

            adjacency.TryAdd(productId, []);
            indegree.TryAdd(productId, 0);
        }

        foreach (var connection in connections)
        {
            EnsureNode(connection.FromProductId, firstAppearance, adjacency, indegree, ref appearanceIndex);
            EnsureNode(connection.ToProductId, firstAppearance, adjacency, indegree, ref appearanceIndex);

            if (adjacency[connection.FromProductId].Add(connection.ToProductId))
            {
                indegree[connection.ToProductId]++;
            }
        }

        var levels = indegree.Keys.ToDictionary(productId => productId, _ => 0);
        var remainingIndegree = indegree.ToDictionary(pair => pair.Key, pair => pair.Value);
        var ready = remainingIndegree
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(productId => firstAppearance[productId])
            .ToList();

        var orderedIds = new List<int>();

        while (ready.Count != 0)
        {
            var currentProductId = ready[0];
            ready.RemoveAt(0);
            orderedIds.Add(currentProductId);

            foreach (var nextProductId in adjacency[currentProductId].OrderBy(productId => firstAppearance[productId]))
            {
                levels[nextProductId] = Math.Max(levels[nextProductId], levels[currentProductId] + 1);
                remainingIndegree[nextProductId]--;
                if (remainingIndegree[nextProductId] == 0)
                {
                    ready.Add(nextProductId);
                    ready.Sort((left, right) => firstAppearance[left].CompareTo(firstAppearance[right]));
                }
            }
        }

        if (orderedIds.Count != remainingIndegree.Count)
        {
            return firstAppearance
                .OrderBy(pair => pair.Value)
                .Select(pair => pair.Key)
                .ToList();
        }

        supportsGraphLayout = true;

        return orderedIds
            .OrderBy(productId => levels[productId])
            .ThenBy(productId => firstAppearance[productId])
            .ToList();
    }

    private static string BuildDeletedProductLabel(string label, bool isDeleted) =>
        isDeleted ? $"{label} [deleted]" : label;

    private static IEnumerable<ServiceCatalogItem> ApplySort(IEnumerable<ServiceCatalogItem> services, string sort) =>
        sort switch
        {
            ServiceSortOptions.Name => services.OrderBy(x => x.Name).ThenByDescending(x => x.UpdatedUtc),
            ServiceSortOptions.NameDesc => services.OrderByDescending(x => x.Name).ThenByDescending(x => x.UpdatedUtc),
            ServiceSortOptions.Owner => services.OrderBy(x => x.Owner).ThenBy(x => x.Name),
            ServiceSortOptions.Lifecycle => services.OrderBy(x => x.LifecycleStatus).ThenBy(x => x.Name),
            ServiceSortOptions.ProductCountDesc => services.OrderByDescending(x => x.ConnectionCount).ThenBy(x => x.Name),
            ServiceSortOptions.UpdatedAsc => services.OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Name),
            _ => services.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Name)
        };

    private static void NormalizeServiceInput(ServiceEditViewModel input)
    {
        input.Owner = NormalizeSelection(input.Owner);
        input.LifecycleStatus = NormalizeSelection(input.LifecycleStatus);
    }

    private static void NormalizeConnectionInput(ServiceConnectionEditorViewModel input)
    {
        input.ConnectionRows = NormalizeConnectionRows(input.ConnectionRows);
    }

    private static List<ServiceConnectionRowInputViewModel> NormalizeConnectionRows(
        IEnumerable<ServiceConnectionRowInputViewModel>? rows)
    {
        var normalized = new List<ServiceConnectionRowInputViewModel>();
        if (rows is null)
        {
            return normalized;
        }

        foreach (var row in rows)
        {
            var fromProductId = row.FromProductId is > 0 ? row.FromProductId : null;
            var toProductId = row.ToProductId is > 0 ? row.ToProductId : null;

            if (fromProductId is null && toProductId is null)
            {
                continue;
            }

            normalized.Add(new ServiceConnectionRowInputViewModel
            {
                FromProductId = fromProductId,
                ToProductId = toProductId
            });
        }

        return normalized;
    }

    private static void HydrateConnectionEditorSummary(ServiceConnectionEditorViewModel model, ServiceCatalogItem service)
    {
        model.ServiceName = service.Name;
        model.ServiceDescription = service.Description;
        model.ServiceOwner = service.Owner;
        model.ServiceLifecycleStatus = service.LifecycleStatus;
        model.UsesLegacyFlow = service.ProductConnections.Count == 0 && service.ProductLinks.Count > 1;
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

    private static void EnsureConnectionRows(ServiceConnectionEditorViewModel model)
    {
        while (model.ConnectionRows.Count < 1)
        {
            model.ConnectionRows.Add(new ServiceConnectionRowInputViewModel());
        }
    }

    private readonly record struct ConnectionPair(int FromProductId, int ToProductId);
}
