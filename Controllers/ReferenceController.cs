using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ReferenceController(
    AppDbContext dbContext,
    TrmWorkbookImportService workbookImportService,
    IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> Index(string? search, int? domainId, int? capabilityId)
    {
        return View(await BuildViewModelAsync(search, domainId, capabilityId, null, TempData["ImportStatusMessage"] as string));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyImport(IFormFile? workbook)
    {
        if (workbook is null || workbook.Length == 0)
        {
            return View("Index", await BuildViewModelAsync(
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

        var verification = await workbookImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
        }

        return View("Index", await BuildViewModelAsync(
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportVerified(string pendingImportToken)
    {
        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ImportStatusMessage"] = "Verify a workbook before importing it.";
            return RedirectToAction(nameof(Index));
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory(), $"{pendingImportToken}.xlsx");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ImportStatusMessage"] = "The verified workbook is no longer available. Upload it again.";
            return RedirectToAction(nameof(Index));
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View("Index", await BuildViewModelAsync(
                null,
                null,
                null,
                new WorkbookImportReviewViewModel
                {
                    Verification = verification
                },
                null));
        }

        var summary = await workbookImportService.ImportAsync(pendingPath);
        System.IO.File.Delete(pendingPath);

        TempData["ImportStatusMessage"] =
            $"TRM model imported. Domains +{summary.DomainsAdded}/{summary.DomainsUpdated} updated, " +
            $"capabilities +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated} updated, " +
            $"components +{summary.ComponentsAdded}/{summary.ComponentsUpdated} updated.";

        return RedirectToAction(nameof(Index));
    }

    private async Task<ReferenceCatalogueViewModel> BuildViewModelAsync(
        string? search,
        int? domainId,
        int? capabilityId,
        WorkbookImportReviewViewModel? importReview,
        string? importStatusMessage)
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
                (x.TechnologyComponentCode != null && x.TechnologyComponentCode.Contains(search)) ||
                x.Name.Contains(search) ||
                (x.Description != null && x.Description.Contains(search)) ||
                (x.ProductExamples != null && x.ProductExamples.Contains(search)));
        }

        return new ReferenceCatalogueViewModel
        {
            Search = search,
            DomainId = domainId,
            CapabilityId = capabilityId,
            Domains = domains,
            Capabilities = capabilities,
            Components = await componentsQuery
                .OrderBy(x => x.IsCustom)
                .ThenBy(x => x.TechnologyComponentCode ?? x.Code)
                .ToListAsync(),
            ImportReview = importReview ?? new WorkbookImportReviewViewModel(),
            ImportStatusMessage = importStatusMessage
        };
    }

    private string EnsurePendingImportDirectory()
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "PendingImports");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
