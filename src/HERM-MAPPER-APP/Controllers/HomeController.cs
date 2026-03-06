using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class HomeController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        var model = new HomeDashboardViewModel
        {
            ProductCount = await dbContext.ProductCatalogItems.CountAsync(),
            CompletedMappings = await dbContext.ProductMappings.CountAsync(x => x.MappingStatus == MappingStatus.Complete),
            ReferenceComponentCount = await dbContext.TrmComponents.CountAsync(x => !x.IsDeleted),
            DomainCount = await dbContext.TrmDomains.CountAsync(),
            CapabilityCount = await dbContext.TrmCapabilities.CountAsync(),
            HasReferenceModel = await dbContext.TrmDomains.AnyAsync(),
            RecentProducts = await dbContext.ProductCatalogItems
                .AsNoTracking()
                .Include(x => x.Mappings)
                .ThenInclude(x => x.TrmComponent)
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(6)
                .ToListAsync()
        };

        return View(model);
    }
}
