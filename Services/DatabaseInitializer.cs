using HERM_MAPPER_APP.Data;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed class DatabaseInitializer(
    AppDbContext dbContext,
    TrmWorkbookImportService workbookImportService,
    SampleRelationshipImportService sampleRelationshipImportService,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (!await dbContext.TrmDomains.AnyAsync(cancellationToken))
        {
            var autoImport = configuration.GetValue("HermWorkbook:AutoImportOnFirstRun", true);
            var workbookPath = configuration["HermWorkbook:Path"];

            if (!autoImport || string.IsNullOrWhiteSpace(workbookPath))
            {
                logger.LogInformation("Skipping HERM workbook import because configuration is disabled or missing.");
                return;
            }

            if (!File.Exists(workbookPath))
            {
                logger.LogWarning("Configured HERM workbook path was not found: {WorkbookPath}", workbookPath);
                return;
            }

            await workbookImportService.ImportAsync(workbookPath, cancellationToken);
            logger.LogInformation("Imported HERM TRM workbook from {WorkbookPath}", workbookPath);
        }

        if (await dbContext.ProductCatalogItems.AnyAsync(cancellationToken))
        {
            return;
        }

        var sampleCsvPath = configuration["SampleRelationships:Path"];
        var autoImportSample = configuration.GetValue("SampleRelationships:AutoImportOnFirstRun", true);

        if (!autoImportSample || string.IsNullOrWhiteSpace(sampleCsvPath))
        {
            return;
        }

        if (!File.Exists(sampleCsvPath))
        {
            logger.LogWarning("Configured sample relationship CSV was not found: {SampleCsvPath}", sampleCsvPath);
            return;
        }

        await sampleRelationshipImportService.ImportAsync(sampleCsvPath, cancellationToken);
        logger.LogInformation("Imported sample relationships from {SampleCsvPath}", sampleCsvPath);
    }
}
