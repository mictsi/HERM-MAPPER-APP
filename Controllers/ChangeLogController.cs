using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ChangeLogController(AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = dbContext.AuditLogEntries
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.Category.Contains(search) ||
                x.Action.Contains(search) ||
                (x.EntityType != null && x.EntityType.Contains(search)) ||
                x.Summary.Contains(search) ||
                (x.Details != null && x.Details.Contains(search)));
        }

        return View(new ChangeLogIndexViewModel
        {
            Search = search,
            Entries = await query
                .OrderByDescending(x => x.OccurredUtc)
                .Take(250)
                .ToListAsync()
        });
    }
}
