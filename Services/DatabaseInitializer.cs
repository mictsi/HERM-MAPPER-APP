using HERM_MAPPER_APP.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        await EnsureSchemaUpToDateAsync(cancellationToken);

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
                logger.LogWarning("Configured HERM workbook path was not found.");
                return;
            }

            await workbookImportService.ImportAsync(workbookPath, cancellationToken);
            logger.LogInformation("Imported HERM TRM workbook from configured startup settings.");
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

    private async Task EnsureSchemaUpToDateAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info('TrmComponents')";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }

            if (!columns.Contains("TechnologyComponentCode"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE TrmComponents ADD COLUMN TechnologyComponentCode TEXT NULL",
                    cancellationToken);
            }

            if (!columns.Contains("IsCustom"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE TrmComponents ADD COLUMN IsCustom INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
