using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

[Authorize(Policy = AppPolicies.CatalogueRead)]
public sealed class HomeController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var model = new HomeDashboardViewModel
        {
            ProductCount = await dbContext.ProductCatalogItems.CountAsync(x => !x.IsDeleted),
            CompletedMappings = await dbContext.ProductMappings.CountAsync(x =>
                x.MappingStatus == MappingStatus.Complete &&
                x.ProductCatalogItem != null &&
                !x.ProductCatalogItem.IsDeleted),
            TrmComponentCount = await dbContext.TrmComponents
                .ForReferenceModel(ReferenceModelKind.Trm)
                .CountAsync(x => !x.IsDeleted),
            TrmDomainCount = await dbContext.TrmDomains
                .ForReferenceModel(ReferenceModelKind.Trm)
                .CountAsync(),
            TrmCapabilityCount = await dbContext.TrmCapabilities
                .ForReferenceModel(ReferenceModelKind.Trm)
                .CountAsync(),
            ArmDomainCount = await dbContext.ArmDomains.CountAsync(),
            ArmCapabilityCount = await dbContext.ArmCapabilities.CountAsync(),
            ArmComponentCount = await dbContext.ArmComponents.CountAsync(x => !x.IsDeleted),
            BrmDomainCount = await dbContext.BrmDomains.CountAsync(),
            BrmCapabilityCount = await dbContext.BrmCapabilities.CountAsync(),
            BrmComponentCount = await dbContext.BrmComponents.CountAsync(x => !x.IsDeleted),
            RecentProducts = await dbContext.ProductCatalogItems
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Include(x => x.Mappings)
                .ThenInclude(x => x.TrmComponent)
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(6)
                .ToListAsync()
        };

        return View(model);
    }
}
