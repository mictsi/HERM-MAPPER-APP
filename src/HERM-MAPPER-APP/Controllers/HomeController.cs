using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class HomeController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> IndexAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

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
