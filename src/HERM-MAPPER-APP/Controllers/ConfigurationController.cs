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

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class ConfigurationController(
    AppDbContext dbContext,
    AppSettingsService appSettingsService,
    ConfigurableFieldService configurableFieldService,
    AuditLogService auditLogService,
    RemoteSqlImportService remoteSqlImportService,
    TrmWorkbookImportService workbookImportService,
    SampleRelationshipImportService sampleRelationshipImportService,
    IWebHostEnvironment environment) : Controller
{
    private const string DisplayTimeZoneSectionKey = "display-time-zone";
    private const string CatalogueImportSectionKey = "catalogue-import";
    private const string ProductImportSectionKey = "product-import";

    public async Task<IActionResult> Index(string? expandedFieldName = null, string? openSection = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View(await BuildViewModelAsync(
            expandedFieldName: NormalizeExpandedFieldName(expandedFieldName),
            openRemoteSqlImportSection: string.Equals(openSection, RemoteSqlImportService.SectionKey, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IActionResult> ImportData(string? openSection = null)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        return View(await BuildViewModelAsync(
            openRemoteSqlImportSection: string.Equals(openSection, RemoteSqlImportService.SectionKey, StringComparison.OrdinalIgnoreCase)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyCatalogueImport(IFormFile? workbook, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (workbook is null || workbook.Length == 0)
        {
            return View(nameof(ImportData), await BuildViewModelAsync(
                catalogueImportReview: BuildCatalogueErrorReview("Choose an .xlsx workbook before verifying the import.", modelKind: modelKind),
                catalogueImportModelKind: modelKind,
                errorSectionKey: CatalogueImportSectionKey));
        }

        if (!string.Equals(Path.GetExtension(workbook.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return View(nameof(ImportData), await BuildViewModelAsync(
                catalogueImportReview: BuildCatalogueErrorReview("Only Excel .xlsx workbooks are supported.", workbook.FileName, modelKind),
                catalogueImportModelKind: modelKind,
                errorSectionKey: CatalogueImportSectionKey));
        }

        var pendingImportToken = Guid.NewGuid().ToString("N");
        var pendingPath = Path.Combine(EnsurePendingImportDirectory("catalogue"), $"{pendingImportToken}.xlsx");

        await using (var stream = System.IO.File.Create(pendingPath))
        {
            await workbook.CopyToAsync(stream);
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath, modelKind);
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

        return View(nameof(ImportData), await BuildViewModelAsync(
            catalogueImportModelKind: modelKind,
            catalogueImportReview: new WorkbookImportReviewViewModel
            {
                ModelKind = modelKind,
                PendingImportToken = verification.IsValid ? pendingImportToken : null,
                UploadedFileName = workbook.FileName,
                Verification = verification
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportVerifiedCatalogue(string pendingImportToken, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ConfigurationError"] = "Verify a catalogue workbook before importing it.";
            TempData["ConfigurationErrorSection"] = CatalogueImportSectionKey;
            return RedirectToImportData();
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory("catalogue"), $"{pendingImportToken}.xlsx");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ConfigurationError"] = "The verified catalogue workbook is no longer available. Upload it again.";
            TempData["ConfigurationErrorSection"] = CatalogueImportSectionKey;
            return RedirectToImportData();
        }

        var verification = await workbookImportService.VerifyAsync(pendingPath, modelKind);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View(nameof(ImportData), await BuildViewModelAsync(
                catalogueImportModelKind: modelKind,
                catalogueImportReview: new WorkbookImportReviewViewModel
                {
                    ModelKind = modelKind,
                    Verification = verification
                },
                errorSectionKey: CatalogueImportSectionKey));
        }

        var summary = await workbookImportService.ImportAsync(pendingPath, modelKind);
        System.IO.File.Delete(pendingPath);

        await auditLogService.WriteAsync(
            "Configuration",
            "ImportCatalogue",
            "TrmWorkbook",
            null,
            $"Imported verified {summary.ModelDisplayName} workbook.",
            $"{summary.DomainLabel} +{summary.DomainsAdded}/{summary.DomainsUpdated}, {summary.CapabilityLabel.ToLowerInvariant()} +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated}, {summary.ComponentLabel.ToLowerInvariant()} +{summary.ComponentsAdded}/{summary.ComponentsUpdated}.");

        TempData["ConfigurationStatusMessage"] =
            $"{ReferenceModelCatalog.GetShortName(modelKind)} catalogue imported. {summary.DomainLabel} +{summary.DomainsAdded}/{summary.DomainsUpdated} updated, " +
            $"{summary.CapabilityLabel.ToLowerInvariant()} +{summary.CapabilitiesAdded}/{summary.CapabilitiesUpdated} updated, " +
            $"{summary.ComponentLabel.ToLowerInvariant()} +{summary.ComponentsAdded}/{summary.ComponentsUpdated} updated.";

        return RedirectToImportData();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbortCatalogueImport(string pendingImportToken, ReferenceModelKind modelKind = ReferenceModelKind.Trm)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        DeletePendingImport("catalogue", pendingImportToken, ".xlsx");
        await auditLogService.WriteAsync(
            "Configuration",
            "AbortCatalogueImport",
            "TrmWorkbook",
            null,
            $"Aborted pending {ReferenceModelCatalog.GetShortName(modelKind)} catalogue import.");
        TempData["ConfigurationStatusMessage"] = $"{ReferenceModelCatalog.GetShortName(modelKind)} catalogue import was aborted.";
        return RedirectToImportData();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyProductImport(IFormFile? csvFile)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (csvFile is null || csvFile.Length == 0)
        {
            return View(nameof(ImportData), await BuildViewModelAsync(
                productImportReview: BuildProductErrorReview("Choose a CSV file before verifying the import."),
                errorSectionKey: ProductImportSectionKey));
        }

        if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return View(nameof(ImportData), await BuildViewModelAsync(
                productImportReview: BuildProductErrorReview("Only .csv files are supported for product import.", csvFile.FileName),
                errorSectionKey: ProductImportSectionKey));
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

        return View(nameof(ImportData), await BuildViewModelAsync(
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
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(pendingImportToken))
        {
            TempData["ConfigurationError"] = "Verify a product CSV before importing it.";
            TempData["ConfigurationErrorSection"] = ProductImportSectionKey;
            return RedirectToImportData();
        }

        var pendingPath = Path.Combine(EnsurePendingImportDirectory("products"), $"{pendingImportToken}.csv");
        if (!System.IO.File.Exists(pendingPath))
        {
            TempData["ConfigurationError"] = "The verified product CSV is no longer available. Upload it again.";
            TempData["ConfigurationErrorSection"] = ProductImportSectionKey;
            return RedirectToImportData();
        }

        var verification = await sampleRelationshipImportService.VerifyAsync(pendingPath);
        if (!verification.IsValid)
        {
            System.IO.File.Delete(pendingPath);
            return View(nameof(ImportData), await BuildViewModelAsync(
                productImportReview: new ProductImportReviewViewModel
                {
                    Verification = verification
                },
                errorSectionKey: ProductImportSectionKey));
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

        return RedirectToImportData();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbortProductImport(string pendingImportToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        DeletePendingImport("products", pendingImportToken, ".csv");
        await auditLogService.WriteAsync(
            "Configuration",
            "AbortProductImport",
            nameof(ProductCatalogItem),
            null,
            "Aborted pending product import.");
        TempData["ConfigurationStatusMessage"] = "Product import was aborted.";
        return RedirectToImportData();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOption(AddConfigurationOptionInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        input.FieldName = input.FieldName?.Trim() ?? string.Empty;
        input.Value = input.Value?.Trim() ?? string.Empty;

        if (!ConfigurableFieldNames.IsSupported(input.FieldName))
        {
            TempData["ConfigurationError"] = "That field is not supported.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex();
        }

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            TempData["ConfigurationError"] = "Enter a value before saving.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex(input.FieldName);
        }

        var caseInsensitiveCollation = AppDatabaseCollations.GetCaseInsensitive(dbContext.Database);

        var exists = await dbContext.ConfigurableFieldOptions.AnyAsync(x =>
            x.FieldName == input.FieldName &&
            EF.Functions.Collate(x.Value, caseInsensitiveCollation) == input.Value);

        if (exists)
        {
            TempData["ConfigurationError"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} value '{input.Value}' already exists.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex(input.FieldName);
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
        configurableFieldService.InvalidateOptions(option.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Create",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Added configuration value '{option.Value}' to {option.FieldName}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} value '{option.Value}' was added.";
        return RedirectToIndex(input.FieldName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOptionOrder(UpdateConfigurationOptionOrderInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var option = await dbContext.ConfigurableFieldOptions.FindAsync(input.Id);
        if (option is null)
        {
            return RedirectToIndex();
        }

        var fieldOptions = await dbContext.ConfigurableFieldOptions
            .Where(x => x.FieldName == option.FieldName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync();

        fieldOptions.RemoveAll(x => x.Id == option.Id);

        var targetIndex = Math.Clamp(input.SortOrder ?? 1, 1, fieldOptions.Count + 1) - 1;
        fieldOptions.Insert(targetIndex, option);

        for (var index = 0; index < fieldOptions.Count; index++)
        {
            fieldOptions[index].SortOrder = index + 1;
        }

        await dbContext.SaveChangesAsync();
        configurableFieldService.InvalidateOptions(option.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Reorder",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Changed order for configuration value '{option.Value}' in {option.FieldName}.",
            $"New position: {option.SortOrder}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} order was updated.";
        return RedirectToIndex(option.FieldName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOption(UpdateConfigurationOptionValueInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var option = await dbContext.ConfigurableFieldOptions.FindAsync(input.Id);
        if (option is null)
        {
            return RedirectToIndex();
        }

        input.Value = input.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input.Value))
        {
            TempData["ConfigurationError"] = "Enter a value before saving.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(option.FieldName);
            return RedirectToIndex(option.FieldName);
        }

        var caseInsensitiveCollation = AppDatabaseCollations.GetCaseInsensitive(dbContext.Database);
        var duplicateExists = await dbContext.ConfigurableFieldOptions.AnyAsync(x =>
            x.FieldName == option.FieldName &&
            x.Id != option.Id &&
            EF.Functions.Collate(x.Value, caseInsensitiveCollation) == input.Value);

        if (duplicateExists)
        {
            TempData["ConfigurationError"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} value '{input.Value}' already exists.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(option.FieldName);
            return RedirectToIndex(option.FieldName);
        }

        var previousValue = option.Value;
        option.Value = input.Value;

        await dbContext.SaveChangesAsync();
        configurableFieldService.InvalidateOptions(option.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Update",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Updated configuration value '{previousValue}' to '{option.Value}' in {option.FieldName}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} value updated to '{option.Value}'.";
        return RedirectToIndex(option.FieldName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderOptions(ReorderConfigurationOptionsInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        input.FieldName = input.FieldName?.Trim() ?? string.Empty;
        if (!ConfigurableFieldNames.IsSupported(input.FieldName))
        {
            TempData["ConfigurationError"] = "That field is not supported.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex();
        }

        var orderedIds = input.OrderedIds
            .Distinct()
            .ToList();

        if (orderedIds.Count == 0)
        {
            TempData["ConfigurationError"] = "Add at least one value before saving the order.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex(input.FieldName);
        }

        var options = await dbContext.ConfigurableFieldOptions
            .Where(x => x.FieldName == input.FieldName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync();

        if (options.Count != orderedIds.Count || options.Any(option => !orderedIds.Contains(option.Id)))
        {
            TempData["ConfigurationError"] = "The value list changed before the new order was saved. Refresh and try again.";
            TempData["ConfigurationErrorSection"] = BuildFieldSectionKey(input.FieldName);
            return RedirectToIndex(input.FieldName);
        }

        var optionsById = options.ToDictionary(option => option.Id);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            optionsById[orderedIds[index]].SortOrder = index + 1;
        }

        await dbContext.SaveChangesAsync();
        configurableFieldService.InvalidateOptions(input.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Reorder",
            nameof(ConfigurableFieldOption),
            null,
            $"Updated value order for {input.FieldName}.",
            $"Order: {string.Join(", ", orderedIds)}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} order was updated.";
        return RedirectToIndex(input.FieldName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOption(int id)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var option = await dbContext.ConfigurableFieldOptions.FindAsync(id);
        if (option is null)
        {
            return RedirectToIndex();
        }

        dbContext.ConfigurableFieldOptions.Remove(option);
        await dbContext.SaveChangesAsync();
        await NormalizeSortOrderAsync(option.FieldName);
        configurableFieldService.InvalidateOptions(option.FieldName);
        await auditLogService.WriteAsync(
            "Configuration",
            "Delete",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Removed configuration value '{option.Value}' from {option.FieldName}.");

        TempData["ConfigurationStatusMessage"] = $"{ConfigurableFieldNames.GetLabel(option.FieldName)} value '{option.Value}' was removed.";
        return RedirectToIndex(option.FieldName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDisplayTimeZone(UpdateDisplayTimeZoneInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        input.TimeZoneId = input.TimeZoneId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(input.TimeZoneId))
        {
            TempData["ConfigurationError"] = "Choose a time zone before saving.";
            TempData["ConfigurationErrorSection"] = DisplayTimeZoneSectionKey;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            TempData["ConfigurationError"] = $"The time zone '{input.TimeZoneId}' is not available on this server.";
            TempData["ConfigurationErrorSection"] = DisplayTimeZoneSectionKey;
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidTimeZoneException)
        {
            TempData["ConfigurationError"] = $"The time zone '{input.TimeZoneId}' is invalid on this server.";
            TempData["ConfigurationErrorSection"] = DisplayTimeZoneSectionKey;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRemoteSqlImportConfiguration(RemoteSqlImportInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedInput = NormalizeRemoteSqlImportInput(input);
        var result = await remoteSqlImportService.SaveSettingsAsync(MapRemoteSqlImportInput(normalizedInput));

        if (!result.IsSuccess)
        {
            return View(nameof(ImportData), await BuildViewModelAsync(
                errorMessage: result.Message,
                errorSectionKey: RemoteSqlImportService.SectionKey,
                remoteSqlInput: normalizedInput,
                openRemoteSqlImportSection: true));
        }

        TempData["ConfigurationStatusMessage"] = result.Message;
        if (!string.IsNullOrWhiteSpace(result.SavedUserNameClearText))
        {
            TempData["RemoteSqlImportSavedUserName"] = result.SavedUserNameClearText;
        }

        if (!string.IsNullOrWhiteSpace(result.SavedPasswordClearText))
        {
            TempData["RemoteSqlImportSavedPassword"] = result.SavedPasswordClearText;
        }

        return RedirectToImportData(RemoteSqlImportService.SectionKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestRemoteSqlImportConnection(RemoteSqlImportInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedInput = NormalizeRemoteSqlImportInput(input);
        var result = await remoteSqlImportService.TestConnectionAsync(MapRemoteSqlImportInput(normalizedInput));

        var testViewModel = new RemoteSqlImportConnectionTestViewModel
        {
            IsSuccess = result.IsSuccess,
            Summary = result.Message,
            RemoteProductCount = result.RemoteProductCount,
            RemoteMappingCount = result.RemoteMappingCount,
            OwnersTableAvailable = result.OwnersTableAvailable,
            Errors = result.Errors,
            Warnings = result.Warnings
        };

        return View(nameof(ImportData), await BuildViewModelAsync(
            statusMessage: result.IsSuccess ? result.Message : null,
            errorMessage: result.IsSuccess ? null : result.Message,
            errorSectionKey: result.IsSuccess ? null : RemoteSqlImportService.SectionKey,
            remoteSqlInput: normalizedInput,
            remoteSqlTestResult: testViewModel,
            openRemoteSqlImportSection: true));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunRemoteSqlImportNow()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await remoteSqlImportService.RunManualImportAsync();
        if (result.IsSuccess)
        {
            TempData["ConfigurationStatusMessage"] = result.Message;
        }
        else
        {
            TempData["ConfigurationError"] = result.Message;
            TempData["ConfigurationErrorSection"] = RemoteSqlImportService.SectionKey;
        }

        return RedirectToImportData(RemoteSqlImportService.SectionKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRemoteSqlImportEnabled(bool isEnabled)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        TempData["ConfigurationStatusMessage"] = await remoteSqlImportService.SetImportEnabledAsync(isEnabled);
        return RedirectToImportData(RemoteSqlImportService.SectionKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearRemoteSqlImportConfiguration()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        TempData.Remove("RemoteSqlImportSavedUserName");
        TempData.Remove("RemoteSqlImportSavedPassword");
        TempData["ConfigurationStatusMessage"] = await remoteSqlImportService.ClearConfigurationAsync();
        return RedirectToImportData(RemoteSqlImportService.SectionKey);
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
        ProductImportReviewViewModel? productImportReview = null,
        string? expandedFieldName = null,
        ReferenceModelKind catalogueImportModelKind = ReferenceModelKind.Trm,
        string? statusMessage = null,
        string? errorMessage = null,
        string? errorSectionKey = null,
        RemoteSqlImportInputModel? remoteSqlInput = null,
        RemoteSqlImportConnectionTestViewModel? remoteSqlTestResult = null,
        bool openRemoteSqlImportSection = false)
    {
        var fields = new List<ConfigurationFieldGroupViewModel>();
        var displayTimeZoneId = await appSettingsService.GetValueAsync(
            AppSettingKeys.DisplayTimeZone,
            AppSettingDefaults.DisplayTimeZone);
        var remoteSqlSettings = await remoteSqlImportService.GetSettingsAsync();
        var savedUserNameClearText = TempData["RemoteSqlImportSavedUserName"] as string;
        var savedPasswordClearText = TempData["RemoteSqlImportSavedPassword"] as string;
        var effectiveRemoteSqlInput = remoteSqlInput ?? new RemoteSqlImportInputModel
        {
            ServerName = remoteSqlSettings.ServerName,
            Port = remoteSqlSettings.Port,
            DatabaseName = remoteSqlSettings.DatabaseName,
            Encrypt = remoteSqlSettings.Encrypt,
            TrustServerCertificate = remoteSqlSettings.TrustServerCertificate,
            UseIntegratedSecurity = remoteSqlSettings.UseIntegratedSecurity,
            ScheduleHours = remoteSqlSettings.ScheduleHours
        };

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
            StatusMessage = statusMessage ?? TempData["ConfigurationStatusMessage"] as string,
            ErrorMessage = errorMessage ?? TempData["ConfigurationError"] as string,
            ErrorSectionKey = NormalizeSectionKey(errorSectionKey ?? TempData["ConfigurationErrorSection"] as string),
            ExpandedFieldName = ConfigurableFieldNames.IsSupported(expandedFieldName)
                ? expandedFieldName
                : null,
            OpenRemoteSqlImportSection = openRemoteSqlImportSection || remoteSqlTestResult is not null || !string.IsNullOrWhiteSpace(savedUserNameClearText) || !string.IsNullOrWhiteSpace(savedPasswordClearText),
            DisplayTimeZoneId = displayTimeZoneId,
            AvailableTimeZones = BuildTimeZoneOptions(displayTimeZoneId),
            CatalogueImportModelKind = catalogueImportModelKind,
            CatalogueImportModelOptions = BuildCatalogueImportModelOptions(catalogueImportModelKind),
            CatalogueImportReview = catalogueImportReview ?? new WorkbookImportReviewViewModel(),
            ProductImportReview = productImportReview ?? new ProductImportReviewViewModel(),
            RemoteSqlImport = new RemoteSqlImportSectionViewModel
            {
                Input = effectiveRemoteSqlInput,
                ScheduleOptions = BuildRemoteSqlScheduleOptions(effectiveRemoteSqlInput.ScheduleHours),
                IsEnabled = remoteSqlSettings.IsEnabled,
                IsConfigured = remoteSqlSettings.IsConfigured,
                HasSavedUserName = remoteSqlSettings.HasSavedUserName,
                HasSavedPassword = remoteSqlSettings.HasSavedPassword,
                SavedUserNameDisplay = remoteSqlSettings.MaskedUserName,
                SavedPasswordDisplay = remoteSqlSettings.MaskedPassword,
                ScheduleSummary = remoteSqlSettings.ScheduleLabel,
                StatusSummary = remoteSqlSettings.StatusLabel,
                LastMessage = remoteSqlSettings.LastMessage,
                LastAttemptUtc = remoteSqlSettings.LastAttemptUtc,
                LastSuccessUtc = remoteSqlSettings.LastSuccessUtc,
                NextScheduledRunUtc = remoteSqlSettings.NextScheduledRunUtc,
                TestResult = remoteSqlTestResult,
                SavedUserNameClearText = savedUserNameClearText,
                SavedPasswordClearText = savedPasswordClearText
            },
            Fields = fields
        };
    }

    private static List<SelectListItem> BuildTimeZoneOptions(string selectedTimeZoneId) =>
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

    private static List<SelectListItem> BuildCatalogueImportModelOptions(ReferenceModelKind selectedModelKind) =>
        ReferenceModelCatalog.All
            .Select(modelKind => new SelectListItem
            {
                Value = modelKind.ToString(),
                Text = $"{ReferenceModelCatalog.GetShortName(modelKind)} - {ReferenceModelCatalog.GetDisplayName(modelKind)}",
                Selected = modelKind == selectedModelKind
            })
            .ToList();

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absoluteOffset = offset.Duration();
        return $"{sign}{absoluteOffset:hh\\:mm}";
    }

    private static List<SelectListItem> BuildRemoteSqlScheduleOptions(int selectedScheduleHours) =>
        RemoteSqlImportService.GetAllowedScheduleHours()
            .Select(hours => new SelectListItem
            {
                Value = hours.ToString(CultureInfo.InvariantCulture),
                Text = RemoteSqlImportService.BuildScheduleLabel(hours),
                Selected = hours == selectedScheduleHours
            })
            .ToList();

    private static WorkbookImportReviewViewModel BuildCatalogueErrorReview(string errorMessage, string? uploadedFileName = null, ReferenceModelKind modelKind = ReferenceModelKind.Trm) =>
        new()
        {
            ModelKind = modelKind,
            UploadedFileName = uploadedFileName,
            Verification = new TrmWorkbookVerificationResult
            {
                ModelKind = modelKind,
                Errors = [errorMessage]
            }
        };

    private static ProductImportReviewViewModel BuildProductErrorReview(string errorMessage, string? uploadedFileName = null) =>
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

    private RedirectToActionResult RedirectToIndex(string? expandedFieldName = null) =>
        ConfigurableFieldNames.IsSupported(NormalizeExpandedFieldName(expandedFieldName))
            ? RedirectToAction(nameof(Index), new { expandedFieldName = NormalizeExpandedFieldName(expandedFieldName) })
            : RedirectToAction(nameof(Index));

    private RedirectToActionResult RedirectToImportData(string? openSection = null) =>
        string.IsNullOrWhiteSpace(openSection)
            ? RedirectToAction(nameof(ImportData))
            : RedirectToAction(nameof(ImportData), new { openSection = NormalizeSectionKey(openSection) });

    private static string? NormalizeExpandedFieldName(string? expandedFieldName) =>
        string.IsNullOrWhiteSpace(expandedFieldName)
            ? null
            : expandedFieldName.Trim();

    private static string BuildFieldSectionKey(string? fieldName) =>
        $"field:{fieldName?.Trim()}";

    private static string? NormalizeSectionKey(string? sectionKey) =>
        string.IsNullOrWhiteSpace(sectionKey)
            ? null
            : sectionKey.Trim();

    private static RemoteSqlImportInputModel NormalizeRemoteSqlImportInput(RemoteSqlImportInputModel input)
    {
        input.ServerName = input.ServerName?.Trim() ?? string.Empty;
        input.DatabaseName = input.DatabaseName?.Trim() ?? string.Empty;
        input.UserName = input.UserName?.Trim();
        input.Password ??= string.Empty;
        return input;
    }

    private static RemoteSqlImportConfigurationInput MapRemoteSqlImportInput(RemoteSqlImportInputModel input) =>
        new(
            input.ServerName,
            input.Port,
            input.DatabaseName,
            input.Encrypt,
            input.TrustServerCertificate,
            input.UseIntegratedSecurity,
            string.IsNullOrWhiteSpace(input.UserName) ? null : input.UserName,
            string.IsNullOrWhiteSpace(input.Password) ? null : input.Password,
            input.ScheduleHours);
}
