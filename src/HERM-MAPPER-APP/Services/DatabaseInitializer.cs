using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
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
        await EnsureConfigurableFieldOptionsTableAsync(cancellationToken);
        await EnsureDefaultConfigurableFieldOptionsAsync(cancellationToken);

        if (dbContext.Database.IsSqlite())
        {
            await EnsureSqliteSchemaUpToDateAsync(cancellationToken);
        }

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

    private async Task EnsureSqliteSchemaUpToDateAsync(CancellationToken cancellationToken)
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

            if (!columns.Contains("IsDeleted"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE TrmComponents ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!columns.Contains("DeletedUtc"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE TrmComponents ADD COLUMN DeletedUtc TEXT NULL",
                    cancellationToken);
            }

            if (!columns.Contains("DeletedReason"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE TrmComponents ADD COLUMN DeletedReason TEXT NULL",
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "TrmComponentCapabilityLinks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrmComponentCapabilityLinks" PRIMARY KEY AUTOINCREMENT,
                    "TrmComponentId" INTEGER NOT NULL,
                    "TrmCapabilityId" INTEGER NOT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_TrmComponentCapabilityLinks_TrmCapabilities_TrmCapabilityId" FOREIGN KEY ("TrmCapabilityId") REFERENCES "TrmCapabilities" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_TrmComponentCapabilityLinks_TrmComponents_TrmComponentId" FOREIGN KEY ("TrmComponentId") REFERENCES "TrmComponents" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrmComponentCapabilityLinks_TrmComponentId_TrmCapabilityId"
                ON "TrmComponentCapabilityLinks" ("TrmComponentId", "TrmCapabilityId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "TrmComponentCapabilityLinks" ("TrmComponentId", "TrmCapabilityId", "CreatedUtc")
                SELECT c."Id", c."ParentCapabilityId", CURRENT_TIMESTAMP
                FROM "TrmComponents" c
                WHERE c."ParentCapabilityId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "TrmComponentCapabilityLinks" l
                      WHERE l."TrmComponentId" = c."Id"
                        AND l."TrmCapabilityId" = c."ParentCapabilityId"
                  )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "TrmComponentVersions" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrmComponentVersions" PRIMARY KEY AUTOINCREMENT,
                    "TrmComponentId" INTEGER NOT NULL,
                    "VersionNumber" INTEGER NOT NULL,
                    "ChangeType" TEXT NOT NULL,
                    "ModelCode" TEXT NULL,
                    "TechnologyComponentCode" TEXT NULL,
                    "Name" TEXT NOT NULL,
                    "IsCustom" INTEGER NOT NULL,
                    "IsDeleted" INTEGER NOT NULL,
                    "CapabilityCodes" TEXT NULL,
                    "CapabilityNames" TEXT NULL,
                    "Description" TEXT NULL,
                    "Comments" TEXT NULL,
                    "ProductExamples" TEXT NULL,
                    "Details" TEXT NULL,
                    "ChangedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_TrmComponentVersions_TrmComponents_TrmComponentId" FOREIGN KEY ("TrmComponentId") REFERENCES "TrmComponents" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrmComponentVersions_TrmComponentId_VersionNumber"
                ON "TrmComponentVersions" ("TrmComponentId", "VersionNumber")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "AuditLogEntries" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogEntries" PRIMARY KEY AUTOINCREMENT,
                    "Category" TEXT NOT NULL,
                    "Action" TEXT NOT NULL,
                    "EntityType" TEXT NULL,
                    "EntityId" INTEGER NULL,
                    "Summary" TEXT NOT NULL,
                    "Details" TEXT NULL,
                    "OccurredUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_AuditLogEntries_OccurredUtc"
                ON "AuditLogEntries" ("OccurredUtc")
                """,
                cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task EnsureConfigurableFieldOptionsTableAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ConfigurableFieldOptions" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ConfigurableFieldOptions" PRIMARY KEY AUTOINCREMENT,
                    "FieldName" TEXT NOT NULL,
                    "Value" TEXT NOT NULL,
                    "CreatedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ConfigurableFieldOptions_FieldName_Value"
                ON "ConfigurableFieldOptions" ("FieldName", "Value")
                """,
                cancellationToken);
        }
        else if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ConfigurableFieldOptions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ConfigurableFieldOptions] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ConfigurableFieldOptions] PRIMARY KEY,
                        [FieldName] NVARCHAR(80) NOT NULL,
                        [Value] NVARCHAR(120) NOT NULL,
                        [CreatedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ConfigurableFieldOptions_FieldName_Value'
                      AND object_id = OBJECT_ID(N'[ConfigurableFieldOptions]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ConfigurableFieldOptions_FieldName_Value]
                    ON [ConfigurableFieldOptions] ([FieldName], [Value]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureDefaultConfigurableFieldOptionsAsync(CancellationToken cancellationToken)
    {
        var existingLifecycleStatuses = await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == ConfigurableFieldNames.LifecycleStatus)
            .Select(x => x.Value)
            .ToListAsync(cancellationToken);

        var missingLifecycleStatuses = ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus)
            .Where(value => existingLifecycleStatuses.All(existing => !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            .Select(value => new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = value,
                CreatedUtc = DateTime.UtcNow
            })
            .ToList();

        if (missingLifecycleStatuses.Count == 0)
        {
            return;
        }

        dbContext.ConfigurableFieldOptions.AddRange(missingLifecycleStatuses);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
