using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ReferenceController(AppDbContext dbContext, IConfiguration configuration) : Controller
{
    public async Task<IActionResult> Index(string? search, int? domainId, int? capabilityId)
    {
        var domains = await dbContext.TrmDomains
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync();

        var capabilitiesQuery = dbContext.TrmCapabilities
            .AsNoTracking()
            .Include(x => x.ParentDomain)
            .AsQueryable();

        if (domainId.HasValue)
        {
            capabilitiesQuery = capabilitiesQuery.Where(x => x.ParentDomainId == domainId);
        }

        var capabilities = await capabilitiesQuery
            .OrderBy(x => x.Code)
            .ToListAsync();

        var componentsQuery = dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.ParentCapability)
            .ThenInclude(x => x!.ParentDomain)
            .AsQueryable();

        if (domainId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.ParentCapability!.ParentDomainId == domainId);
        }

        if (capabilityId.HasValue)
        {
            componentsQuery = componentsQuery.Where(x => x.ParentCapabilityId == capabilityId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            componentsQuery = componentsQuery.Where(x =>
                x.Code.Contains(search) ||
                x.Name.Contains(search) ||
                (x.Description != null && x.Description.Contains(search)) ||
                (x.ProductExamples != null && x.ProductExamples.Contains(search)));
        }

        var model = new ReferenceCatalogueViewModel
        {
            Search = search,
            DomainId = domainId,
            CapabilityId = capabilityId,
            WorkbookPath = configuration["HermWorkbook:Path"],
            Domains = domains,
            Capabilities = capabilities,
            Components = await componentsQuery.OrderBy(x => x.Code).ToListAsync()
        };

        return View(model);
    }
}
