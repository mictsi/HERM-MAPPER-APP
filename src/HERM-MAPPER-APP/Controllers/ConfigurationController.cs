using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class ConfigurationController(
    AppDbContext dbContext,
    AppSettingsService appSettingsService,
    ConfigurableFieldService configurableFieldService,
    AuditLogService auditLogService,
    TrmWorkbookImportService workbookImportService,
    SampleRelationshipImportService sampleRelationshipImportService,
    IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await BuildViewModelAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyCatalogueImport(IFormFile? workbook)
    {
        if (workbook is null || workbook.Length == 0)
        {
            return View("Index", await BuildViewModelAsync(
                catalogueImportReview: BuildCatalogueErrorReview("Choose an .xlsx workbook before verifying the import.")));
        }

        if (!string.Equals(Path.GetExtension(workbook.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return View("Index", await BuildViewModelAsync(
                catalogueImportReview: BuildCatalogueErrorReview("Only Excel .xlsx workbooks are supported.", workbook.FileName)));
        }

        var pendingImportToken = Guid.NewGuid().ToString("N");
        var pendingPath = Path.Combine(EnsurePendingImportDirectory("catalogue"), $"{pendingImportToken}.xlsx");

        await using (var stream = System.IO.File.Create(pendingPath))
        {
            await workbook.CopyToAsync(stream);
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
        }

        await auditLogService.WriteAsync(
            "Configuration",
            "VerifyCatalogueImport",
            "TrmWorkbook",
            null,
            $"Verified workbook {workbook.FileName}.",
            verification.IsValid ? "Verification passed." : string.Join(" | ", verification.Errors));

        return View("Index", await BuildViewModelAsync(
            catalogueImportReview: new WorkbookImportReviewViewModel
            {
                PendingImportToken = verification.IsValid ? pendingImportToken : null,
                UploadedFileName = workbook.FileName,
                Verification = verification
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportVerifiedCatalogue(string pendingImportToken)
    {
        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ConfigurationError"] = "Verify a catalogue workbook before importing it.";
            return RedirectToAction(nameof(Index));
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory("catalogue"), $"{pendingImportToken}.xlsx");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ConfigurationError"] = "The verified catalogue workbook is no longer available. Upload it again.";
            return RedirectToAction(nameof(Index));
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View("Index", await BuildViewModelAsync(
                catalogueImportReview: new WorkbookImportReviewViewModel
                {
                    Verification = verification
                }));
        }

        var summary = await workbookImportService.ImportAsync(pendingPath);
        System.IO.File.Delete(pendingPath);

        await auditLogService.WriteAsync(
            "Configuration",
            "ImportCatalogue",
            "TrmWorkbook",
            null,
            "Imported verified catalogue workbook.",
            $"Domains +{summary.DomainsAdded}/{summary.DomainsUpdated}, capabilities +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated}, components +{summary.ComponentsAdded}/{summary.ComponentsUpdated}.");

        TempData["ConfigurationStatusMessage"] =
            $"Catalogue imported. Domains +{summary.DomainsAdded}/{summary.DomainsUpdated} updated, " +
            $"capabilities +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated} updated, " +
            $"components +{summary.ComponentsAdded}/{summary.ComponentsUpdated} updated.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbortCatalogueImport(string pendingImportToken)
    {
        DeletePendingImport("catalogue", pendingImportToken, ".xlsx");
        await auditLogService.WriteAsync(
            "Configuration",
            "AbortCatalogueImport",
            "TrmWorkbook",
            null,
            "Aborted pending catalogue import.");
        TempData["ConfigurationStatusMessage"] = "Catalogue import was aborted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyProductImport(IFormFile? csvFile)
    {
        if (csvFile is null || csvFile.Length == 0)
        {
            return View("Index", await BuildViewModelAsync(
                productImportReview: BuildProductErrorReview("Choose a CSV file before verifying the import.")));
        }

        if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return View("Index", await BuildViewModelAsync(
                productImportReview: BuildProductErrorReview("Only .csv files are supported for product import.", csvFile.FileName)));
        }

        var pendingImportToken = Guid.NewGuid().ToString("N");
        var pendingPath = Path.Combine(EnsurePendingImportDirectory("products"), $"{pendingImportToken}.csv");

        await using (var stream = System.IO.File.Create(pendingPath))
        {
            await csvFile.CopyToAsync(stream);
        }

        var verification = await sampleRelationshipImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
        }

        await auditLogService.WriteAsync(
            "Configuration",
            "VerifyProductImport",
            nameof(ProductCatalogItem),
            null,
            $"Verified product import CSV {csvFile.FileName}.",
            verification.IsValid ? $"Rows read: {verification.RowsRead}." : string.Join(" | ", verification.Errors));

        return View("Index", await BuildViewModelAsync(
            productImportReview: new ProductImportReviewViewModel
            {
                PendingImportToken = verification.IsValid ? pendingImportToken : null,
                UploadedFileName = csvFile.FileName,
                Verification = verification
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportVerifiedProducts(string pendingImportToken)
    {
        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ConfigurationError"] = "Verify a product CSV before importing it.";
            return RedirectToAction(nameof(Index));
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory("products"), $"{pendingImportToken}.csv");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ConfigurationError"] = "The verified product CSV is no longer available. Upload it again.";
            return RedirectToAction(nameof(Index));
        }

        var verification = await sampleRelationshipImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View("Index", await BuildViewModelAsync(
                productImportReview: new ProductImportReviewViewModel
                {
                    Verification = verification
                }));
        }

        var summary = await sampleRelationshipImportService.ImportAsync(pendingPath);
        System.IO.File.Delete(pendingPath);

        await auditLogService.WriteAsync(
            "Configuration",
            "ImportProducts",
            nameof(ProductCatalogItem),
            null,
            "Imported verified product CSV.",
            $"Rows read: {summary.RowsRead}; products added: {summary.ProductsAdded}; existing products matched: {summary.ProductsMatched}; mappings added: {summary.MappingsAdded}; product-only rows: {summary.ProductsOnlyRows}; duplicate mappings skipped: {summary.MappingsSkippedAsDuplicate}; rows skipped: {summary.RowsSkipped}.");

        TempData["ConfigurationStatusMessage"] =
            $"Imported {summary.ProductsAdded} new product(s), matched {summary.ProductsMatched} existing product(s), " +
            $"created {summary.MappingsAdded} mapping(s), and left {summary.ProductsOnlyRows} row(s) as product-only because the hierarchy did not match.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbortProductImport(string pendingImportToken)
    {
        DeletePendingImport("products", pendingImportToken, ".csv");
        await auditLogService.WriteAsync(
            "Configuration",
            "AbortProductImport",
            nameof(ProductCatalogItem),
            null,
            "Aborted pending product import.");
        TempData["ConfigurationStatusMessage"] = "Product import was aborted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOption(AddConfigurationOptionInputModel input)
    {
        input.FieldName = input.FieldName?.Trim() ?? string.Empty;
        input.Value = input.Value?.Trim() ?? string.Empty;

        if (!ConfigurableFieldNames.IsSupported(input.FieldName))
        {
            TempData["ConfigurationError"] = "That field is not supported.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            TempData["ConfigurationError"] = "Enter a value before saving.";
            return RedirectToAction(nameof(Index));
        }

        var exists = await dbContext.ConfigurableFieldOptions.AnyAsync(x =>
            x.FieldName == input.FieldName &&
            x.Value.ToLower() == input.Value.ToLower());

        if (exists)
        {
            TempData["ConfigurationError"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} value '{input.Value}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        var option = new ConfigurableFieldOption
        {
            FieldName = input.FieldName,
            Value = input.Value,
            SortOrder = await GetNextSortOrderAsync(input.FieldName),
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.ConfigurableFieldOptions.Add(option);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Configuration",
            "Create",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Added configuration value '{option.Value}' to {option.FieldName}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} value '{option.Value}' was added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOptionOrder(UpdateConfigurationOptionOrderInputModel input)
    {
        var option = await dbContext.ConfigurableFieldOptions.FindAsync(input.Id);
        if (option is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var fieldOptions = await dbContext.ConfigurableFieldOptions
            .Where(x => x.FieldName == option.FieldName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync();

        fieldOptions.RemoveAll(x => x.Id == option.Id);

        var targetIndex = Math.Clamp(input.SortOrder, 1, fieldOptions.Count + 1) - 1;
        fieldOptions.Insert(targetIndex, option);

        for (var index = 0; index < fieldOptions.Count; index++)
        {
            fieldOptions[index].SortOrder = index + 1;
        }

        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Configuration",
            "Reorder",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Changed order for configuration value '{option.Value}' in {option.FieldName}.",
            $"New position: {option.SortOrder}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} order was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOption(int id)
    {
        var option = await dbContext.ConfigurableFieldOptions.FindAsync(id);
        if (option is null)
        {
            return RedirectToAction(nameof(Index));
        }

        dbContext.ConfigurableFieldOptions.Remove(option);
        await dbContext.SaveChangesAsync();
        await NormalizeSortOrderAsync(option.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Delete",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Removed configuration value '{option.Value}' from {option.FieldName}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} value '{option.Value}' was removed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDisplayTimeZone(UpdateDisplayTimeZoneInputModel input)
    {
        input.TimeZoneId = input.TimeZoneId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input.TimeZoneId))
        {
            TempData["ConfigurationError"] = "Choose a time zone before saving.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            TempData["ConfigurationError"] = $"The time zone '{input.TimeZoneId}' is not available on this server.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidTimeZoneException)
        {
            TempData["ConfigurationError"] = $"The time zone '{input.TimeZoneId}' is invalid on this server.";
            return RedirectToAction(nameof(Index));
        }

        await appSettingsService.SetValueAsync(AppSettingKeys.DisplayTimeZone, input.TimeZoneId);
        await auditLogService.WriteAsync(
            "Configuration",
            "UpdateDisplayTimeZone",
            nameof(AppSetting),
            null,
            $"Updated display time zone to '{input.TimeZoneId}'.");

        TempData["ConfigurationStatusMessage"] = $"Display time zone updated to '{input.TimeZoneId}'.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<int> GetNextSortOrderAsync(string fieldName)
    {
        var maxSortOrder = await dbContext.ConfigurableFieldOptions
            .Where(x => x.FieldName == fieldName)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync();

        return (maxSortOrder ?? 0) + 1;
    }

    private async Task NormalizeSortOrderAsync(string fieldName)
    {
        var options = await dbContext.ConfigurableFieldOptions
            .Where(x => x.FieldName == fieldName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var hasChanges = false;
        for (var index = 0; index < options.Count; index++)
        {
            var expectedSortOrder = index + 1;
            if (options[index].SortOrder == expectedSortOrder)
            {
                continue;
            }

            options[index].SortOrder = expectedSortOrder;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<ConfigurationIndexViewModel> BuildViewModelAsync(
        WorkbookImportReviewViewModel? catalogueImportReview = null,
        ProductImportReviewViewModel? productImportReview = null)
    {
        var fields = new List<ConfigurationFieldGroupViewModel>();
        var displayTimeZoneId = await appSettingsService.GetValueAsync(
            AppSettingKeys.DisplayTimeZone,
            AppSettingDefaults.DisplayTimeZone);

        foreach (var field in ConfigurableFieldNames.All)
        {
            fields.Add(new ConfigurationFieldGroupViewModel
            {
                FieldName = field.Key,
                Label = field.Value,
                Options = await configurableFieldService.GetOptionsAsync(field.Key)
            });
        }

        return new ConfigurationIndexViewModel
        {
            StatusMessage = TempData["ConfigurationStatusMessage"] as string,
            ErrorMessage = TempData["ConfigurationError"] as string,
            DisplayTimeZoneId = displayTimeZoneId,
            AvailableTimeZones = BuildTimeZoneOptions(displayTimeZoneId),
            CatalogueImportReview = catalogueImportReview ?? new WorkbookImportReviewViewModel(),
            ProductImportReview = productImportReview ?? new ProductImportReviewViewModel(),
            Fields = fields
        };
    }

    private static IReadOnlyList<SelectListItem> BuildTimeZoneOptions(string selectedTimeZoneId) =>
        TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(x => x.BaseUtcOffset)
            .ThenBy(x => x.DisplayName)
            .Select(x => new SelectListItem
            {
                Value = x.Id,
                Text = $"(UTC{FormatOffset(x.BaseUtcOffset)}) {x.Id}",
                Selected = string.Equals(x.Id, selectedTimeZoneId, StringComparison.Ordinal)
            })
            .ToList();

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absoluteOffset = offset.Duration();
        return $"{sign}{absoluteOffset:hh\\:mm}";
    }

    private WorkbookImportReviewViewModel BuildCatalogueErrorReview(string errorMessage, string? uploadedFileName = null) =>
        new()
        {
            UploadedFileName = uploadedFileName,
            Verification = new TrmWorkbookVerificationResult
            {
                Errors = [errorMessage]
            }
        };

    private ProductImportReviewViewModel BuildProductErrorReview(string errorMessage, string? uploadedFileName = null) =>
        new()
        {
            UploadedFileName = uploadedFileName,
            Verification = new ProductRelationshipVerificationResult
            {
                Errors = [errorMessage]
            }
        };

    private string EnsurePendingImportDirectory(string importType)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "PendingImports", importType);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private void DeletePendingImport(string importType, string pendingImportToken, string extension)
    {
        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            return;
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory(importType), $"{pendingImportToken}{extension}");
        if (System.IO.File.Exists(pendingPath))
        {
            System.IO.File.Delete(pendingPath);
        }
    }
}
