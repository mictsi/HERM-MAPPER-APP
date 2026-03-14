using HERMMapperApp.Data;
using HERMMapperApp.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Services;

public sealed partial class DatabaseInitializer(
    AppDbContext dbContext,
    TrmWorkbookImportService workbookImportService,
    SampleRelationshipImportService sampleRelationshipImportService,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureServiceTablesAsync(cancellationToken);
        await EnsureProductOwnerTableAsync(cancellationToken);
        await EnsureProductSoftDeleteColumnsAsync(cancellationToken);
        await EnsureServiceSoftDeleteColumnsAsync(cancellationToken);
        await EnsureServiceConnectionLayoutColumnAsync(cancellationToken);
        await EnsureAppSettingsTableAsync(cancellationToken);
        await EnsureUsersTableAsync(cancellationToken);
        await EnsureRoleNormalizationAsync(cancellationToken);
        await EnsureConfigurableFieldOptionsTableAsync(cancellationToken);
        await EnsureDefaultAppSettingsAsync(cancellationToken);
        await EnsureBootstrapAdminUserAsync(cancellationToken);
        await NormalizeConfigurableFieldOptionSortOrdersAsync(cancellationToken);
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
                LogSkippingWorkbookImport(logger);
                return;
            }

            if (!File.Exists(workbookPath))
            {
                LogMissingWorkbookPath(logger);
                return;
            }

            await workbookImportService.ImportAsync(workbookPath, cancellationToken);
            LogImportedWorkbook(logger);
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
            LogMissingSampleRelationshipCsv(logger, sampleCsvPath);
            return;
        }

        await sampleRelationshipImportService.ImportAsync(sampleCsvPath, cancellationToken);
        LogImportedSampleRelationships(logger, sampleCsvPath);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Skipping HERM workbook import because configuration is disabled or missing.")]
    private static partial void LogSkippingWorkbookImport(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Configured HERM workbook path was not found.")]
    private static partial void LogMissingWorkbookPath(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Imported HERM TRM workbook from configured startup settings.")]
    private static partial void LogImportedWorkbook(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Configured sample relationship CSV was not found: {sampleCsvPath}")]
    private static partial void LogMissingSampleRelationshipCsv(ILogger logger, string sampleCsvPath);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Imported sample relationships from {sampleCsvPath}")]
    private static partial void LogImportedSampleRelationships(ILogger logger, string sampleCsvPath);

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

    private async Task EnsureProductOwnerTableAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ProductCatalogItemOwners" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductCatalogItemOwners" PRIMARY KEY AUTOINCREMENT,
                    "ProductCatalogItemId" INTEGER NOT NULL,
                    "OwnerValue" TEXT NOT NULL,
                    CONSTRAINT "FK_ProductCatalogItemOwners_ProductCatalogItems_ProductCatalogItemId" FOREIGN KEY ("ProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductCatalogItemOwners_ProductCatalogItemId_OwnerValue"
                ON "ProductCatalogItemOwners" ("ProductCatalogItemId", "OwnerValue")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ProductCatalogItemOwners_OwnerValue"
                ON "ProductCatalogItemOwners" ("OwnerValue")
                """,
                cancellationToken);

            var legacyOwnerColumnExists = await SqliteColumnExistsAsync("ProductCatalogItems", "Owner", cancellationToken);
            if (legacyOwnerColumnExists)
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "ProductCatalogItemOwners" ("ProductCatalogItemId", "OwnerValue")
                    SELECT p."Id", TRIM(p."Owner")
                    FROM "ProductCatalogItems" p
                    WHERE p."Owner" IS NOT NULL
                      AND TRIM(p."Owner") <> ''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "ProductCatalogItemOwners" o
                          WHERE o."ProductCatalogItemId" = p."Id"
                            AND LOWER(o."OwnerValue") = LOWER(TRIM(p."Owner"))
                      )
                    """,
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ProductCatalogItemOwners]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ProductCatalogItemOwners] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ProductCatalogItemOwners] PRIMARY KEY,
                        [ProductCatalogItemId] INT NOT NULL,
                        [OwnerValue] NVARCHAR(120) NOT NULL,
                        CONSTRAINT [FK_ProductCatalogItemOwners_ProductCatalogItems_ProductCatalogItemId]
                            FOREIGN KEY ([ProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE CASCADE
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ProductCatalogItemOwners_ProductCatalogItemId_OwnerValue'
                      AND object_id = OBJECT_ID(N'[ProductCatalogItemOwners]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ProductCatalogItemOwners_ProductCatalogItemId_OwnerValue]
                    ON [ProductCatalogItemOwners] ([ProductCatalogItemId], [OwnerValue]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ProductCatalogItemOwners_OwnerValue'
                      AND object_id = OBJECT_ID(N'[ProductCatalogItemOwners]')
                )
                BEGIN
                    CREATE INDEX [IX_ProductCatalogItemOwners_OwnerValue]
                    ON [ProductCatalogItemOwners] ([OwnerValue]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ProductCatalogItems]', N'Owner') IS NOT NULL
                BEGIN
                    INSERT INTO [ProductCatalogItemOwners] ([ProductCatalogItemId], [OwnerValue])
                    SELECT p.[Id], LTRIM(RTRIM(p.[Owner]))
                    FROM [ProductCatalogItems] p
                    WHERE p.[Owner] IS NOT NULL
                      AND LTRIM(RTRIM(p.[Owner])) <> N''
                      AND NOT EXISTS (
                          SELECT 1
                          FROM [ProductCatalogItemOwners] o
                          WHERE o.[ProductCatalogItemId] = p.[Id]
                            AND LOWER(o.[OwnerValue]) = LOWER(LTRIM(RTRIM(p.[Owner])))
                      );
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureServiceTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ServiceCatalogItems" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ServiceCatalogItems" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "Owner" TEXT NOT NULL,
                    "LifecycleStatus" TEXT NOT NULL,
                    "ConnectionLayoutJson" TEXT NULL,
                    "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                    "DeletedUtc" TEXT NULL,
                    "DeletedReason" TEXT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ServiceCatalogItemProducts" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ServiceCatalogItemProducts" PRIMARY KEY AUTOINCREMENT,
                    "ServiceCatalogItemId" INTEGER NOT NULL,
                    "ProductCatalogItemId" INTEGER NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    CONSTRAINT "FK_ServiceCatalogItemProducts_ServiceCatalogItems_ServiceCatalogItemId"
                        FOREIGN KEY ("ServiceCatalogItemId") REFERENCES "ServiceCatalogItems" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ServiceCatalogItemProducts_ProductCatalogItems_ProductCatalogItemId"
                        FOREIGN KEY ("ProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ServiceCatalogItemConnections" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ServiceCatalogItemConnections" PRIMARY KEY AUTOINCREMENT,
                    "ServiceCatalogItemId" INTEGER NOT NULL,
                    "FromProductCatalogItemId" INTEGER NOT NULL,
                    "ToProductCatalogItemId" INTEGER NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    CONSTRAINT "FK_ServiceCatalogItemConnections_ServiceCatalogItems_ServiceCatalogItemId"
                        FOREIGN KEY ("ServiceCatalogItemId") REFERENCES "ServiceCatalogItems" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ServiceCatalogItemConnections_ProductCatalogItems_FromProductCatalogItemId"
                        FOREIGN KEY ("FromProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ServiceCatalogItemConnections_ProductCatalogItems_ToProductCatalogItemId"
                        FOREIGN KEY ("ToProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceCatalogItemProducts_ServiceCatalogItemId_SortOrder"
                ON "ServiceCatalogItemProducts" ("ServiceCatalogItemId", "SortOrder")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ServiceCatalogItemProducts_ProductCatalogItemId"
                ON "ServiceCatalogItemProducts" ("ProductCatalogItemId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceCatalogItemConnections_ServiceCatalogItemId_SortOrder"
                ON "ServiceCatalogItemConnections" ("ServiceCatalogItemId", "SortOrder")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceCatalogItemConnections_ServiceCatalogItemId_FromProductCatalogItemId_ToProductCatalogItemId"
                ON "ServiceCatalogItemConnections" ("ServiceCatalogItemId", "FromProductCatalogItemId", "ToProductCatalogItemId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ServiceCatalogItemConnections_FromProductCatalogItemId"
                ON "ServiceCatalogItemConnections" ("FromProductCatalogItemId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ServiceCatalogItemConnections_ToProductCatalogItemId"
                ON "ServiceCatalogItemConnections" ("ToProductCatalogItemId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ServiceCatalogItems_Owner"
                ON "ServiceCatalogItems" ("Owner")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ServiceCatalogItems_LifecycleStatus"
                ON "ServiceCatalogItems" ("LifecycleStatus")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ServiceCatalogItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ServiceCatalogItems] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServiceCatalogItems] PRIMARY KEY,
                        [Name] NVARCHAR(200) NOT NULL,
                        [Description] NVARCHAR(2000) NULL,
                        [Owner] NVARCHAR(120) NOT NULL,
                        [LifecycleStatus] NVARCHAR(80) NOT NULL,
                        [ConnectionLayoutJson] NVARCHAR(MAX) NULL,
                        [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ServiceCatalogItems_IsDeleted] DEFAULT 0,
                        [DeletedUtc] DATETIME2 NULL,
                        [DeletedReason] NVARCHAR(400) NULL,
                        [CreatedUtc] DATETIME2 NOT NULL,
                        [UpdatedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ServiceCatalogItemProducts]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ServiceCatalogItemProducts] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServiceCatalogItemProducts] PRIMARY KEY,
                        [ServiceCatalogItemId] INT NOT NULL,
                        [ProductCatalogItemId] INT NOT NULL,
                        [SortOrder] INT NOT NULL,
                        CONSTRAINT [FK_ServiceCatalogItemProducts_ServiceCatalogItems_ServiceCatalogItemId]
                            FOREIGN KEY ([ServiceCatalogItemId]) REFERENCES [ServiceCatalogItems] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ServiceCatalogItemProducts_ProductCatalogItems_ProductCatalogItemId]
                            FOREIGN KEY ([ProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE CASCADE
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ServiceCatalogItemConnections]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ServiceCatalogItemConnections] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ServiceCatalogItemConnections] PRIMARY KEY,
                        [ServiceCatalogItemId] INT NOT NULL,
                        [FromProductCatalogItemId] INT NOT NULL,
                        [ToProductCatalogItemId] INT NOT NULL,
                        [SortOrder] INT NOT NULL,
                        CONSTRAINT [FK_ServiceCatalogItemConnections_ServiceCatalogItems_ServiceCatalogItemId]
                            FOREIGN KEY ([ServiceCatalogItemId]) REFERENCES [ServiceCatalogItems] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ServiceCatalogItemConnections_ProductCatalogItems_FromProductCatalogItemId]
                            FOREIGN KEY ([FromProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ServiceCatalogItemConnections_ProductCatalogItems_ToProductCatalogItemId]
                            FOREIGN KEY ([ToProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE CASCADE
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemProducts_ServiceCatalogItemId_SortOrder'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemProducts]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ServiceCatalogItemProducts_ServiceCatalogItemId_SortOrder]
                    ON [ServiceCatalogItemProducts] ([ServiceCatalogItemId], [SortOrder]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemConnections_ServiceCatalogItemId_SortOrder'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemConnections]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ServiceCatalogItemConnections_ServiceCatalogItemId_SortOrder]
                    ON [ServiceCatalogItemConnections] ([ServiceCatalogItemId], [SortOrder]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemConnections_ServiceCatalogItemId_FromProductCatalogItemId_ToProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemConnections]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ServiceCatalogItemConnections_ServiceCatalogItemId_FromProductCatalogItemId_ToProductCatalogItemId]
                    ON [ServiceCatalogItemConnections] ([ServiceCatalogItemId], [FromProductCatalogItemId], [ToProductCatalogItemId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemConnections_FromProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemConnections]')
                )
                BEGIN
                    CREATE INDEX [IX_ServiceCatalogItemConnections_FromProductCatalogItemId]
                    ON [ServiceCatalogItemConnections] ([FromProductCatalogItemId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemConnections_ToProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemConnections]')
                )
                BEGIN
                    CREATE INDEX [IX_ServiceCatalogItemConnections_ToProductCatalogItemId]
                    ON [ServiceCatalogItemConnections] ([ToProductCatalogItemId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItemProducts_ProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItemProducts]')
                )
                BEGIN
                    CREATE INDEX [IX_ServiceCatalogItemProducts_ProductCatalogItemId]
                    ON [ServiceCatalogItemProducts] ([ProductCatalogItemId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItems_Owner'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItems]')
                )
                BEGIN
                    CREATE INDEX [IX_ServiceCatalogItems_Owner]
                    ON [ServiceCatalogItems] ([Owner]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ServiceCatalogItems_LifecycleStatus'
                      AND object_id = OBJECT_ID(N'[ServiceCatalogItems]')
                )
                BEGIN
                    CREATE INDEX [IX_ServiceCatalogItems_LifecycleStatus]
                    ON [ServiceCatalogItems] ([LifecycleStatus]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureProductSoftDeleteColumnsAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("ProductCatalogItems", "IsDeleted", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ProductCatalogItems ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ProductCatalogItems", "DeletedUtc", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ProductCatalogItems ADD COLUMN DeletedUtc TEXT NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ProductCatalogItems", "DeletedReason", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ProductCatalogItems ADD COLUMN DeletedReason TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ProductCatalogItems]', N'IsDeleted') IS NULL
                BEGIN
                    ALTER TABLE [ProductCatalogItems]
                    ADD [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ProductCatalogItems_IsDeleted] DEFAULT 0;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ProductCatalogItems]', N'DeletedUtc') IS NULL
                BEGIN
                    ALTER TABLE [ProductCatalogItems]
                    ADD [DeletedUtc] DATETIME2 NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ProductCatalogItems]', N'DeletedReason') IS NULL
                BEGIN
                    ALTER TABLE [ProductCatalogItems]
                    ADD [DeletedReason] NVARCHAR(400) NULL;
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureServiceSoftDeleteColumnsAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("ServiceCatalogItems", "IsDeleted", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ServiceCatalogItems ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ServiceCatalogItems", "DeletedUtc", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ServiceCatalogItems ADD COLUMN DeletedUtc TEXT NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ServiceCatalogItems", "DeletedReason", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ServiceCatalogItems ADD COLUMN DeletedReason TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ServiceCatalogItems]', N'IsDeleted') IS NULL
                BEGIN
                    ALTER TABLE [ServiceCatalogItems]
                    ADD [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ServiceCatalogItems_IsDeleted] DEFAULT 0;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ServiceCatalogItems]', N'DeletedUtc') IS NULL
                BEGIN
                    ALTER TABLE [ServiceCatalogItems]
                    ADD [DeletedUtc] DATETIME2 NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ServiceCatalogItems]', N'DeletedReason') IS NULL
                BEGIN
                    ALTER TABLE [ServiceCatalogItems]
                    ADD [DeletedReason] NVARCHAR(400) NULL;
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureServiceConnectionLayoutColumnAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("ServiceCatalogItems", "ConnectionLayoutJson", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ServiceCatalogItems ADD COLUMN ConnectionLayoutJson TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ServiceCatalogItems]', N'ConnectionLayoutJson') IS NULL
                BEGIN
                    ALTER TABLE [ServiceCatalogItems]
                    ADD [ConnectionLayoutJson] NVARCHAR(MAX) NULL;
                END
                """,
                cancellationToken);
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
                    "SortOrder" INTEGER NOT NULL DEFAULT 0,
                    "CreatedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await EnsureSqliteConfigurableFieldOptionColumnsAsync(cancellationToken);

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
                        [SortOrder] INT NOT NULL CONSTRAINT [DF_ConfigurableFieldOptions_SortOrder] DEFAULT 0,
                        [CreatedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ConfigurableFieldOptions]', N'SortOrder') IS NULL
                BEGIN
                    ALTER TABLE [ConfigurableFieldOptions]
                    ADD [SortOrder] INT NOT NULL CONSTRAINT [DF_ConfigurableFieldOptions_SortOrder] DEFAULT 0;
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

    private async Task EnsureAppSettingsTableAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "AppSettings" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY AUTOINCREMENT,
                    "Key" TEXT NOT NULL,
                    "Value" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppSettings_Key"
                ON "AppSettings" ("Key")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[AppSettings]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AppSettings] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AppSettings] PRIMARY KEY,
                        [Key] NVARCHAR(100) NOT NULL,
                        [Value] NVARCHAR(400) NOT NULL,
                        [UpdatedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AppSettings_Key'
                      AND object_id = OBJECT_ID(N'[AppSettings]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_AppSettings_Key]
                    ON [AppSettings] ([Key]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureUsersTableAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "AppUsers" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AppUsers" PRIMARY KEY AUTOINCREMENT,
                    "GivenName" TEXT NOT NULL,
                    "LastName" TEXT NOT NULL,
                    "Email" TEXT NOT NULL,
                    "UserName" TEXT NOT NULL,
                    "PasswordHash" TEXT NOT NULL,
                    "RoleName" TEXT NOT NULL,
                    "FailedLoginCount" INTEGER NOT NULL DEFAULT 0,
                    "LockoutEndUtc" TEXT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL,
                    "PasswordChangedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await EnsureSqliteUserColumnsAsync(cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_Email"
                ON "AppUsers" ("Email")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_UserName"
                ON "AppUsers" ("UserName")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[AppUsers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AppUsers] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AppUsers] PRIMARY KEY,
                        [GivenName] NVARCHAR(100) NOT NULL,
                        [LastName] NVARCHAR(100) NOT NULL,
                        [Email] NVARCHAR(200) NOT NULL,
                        [UserName] NVARCHAR(100) NOT NULL,
                        [PasswordHash] NVARCHAR(400) NOT NULL,
                        [RoleName] NVARCHAR(40) NOT NULL,
                        [FailedLoginCount] INT NOT NULL CONSTRAINT [DF_AppUsers_FailedLoginCount] DEFAULT 0,
                        [LockoutEndUtc] DATETIME2 NULL,
                        [CreatedUtc] DATETIME2 NOT NULL,
                        [UpdatedUtc] DATETIME2 NOT NULL,
                        [PasswordChangedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AppUsers]', N'FailedLoginCount') IS NULL
                BEGIN
                    ALTER TABLE [AppUsers]
                    ADD [FailedLoginCount] INT NOT NULL CONSTRAINT [DF_AppUsers_FailedLoginCount] DEFAULT 0;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AppUsers]', N'LockoutEndUtc') IS NULL
                BEGIN
                    ALTER TABLE [AppUsers]
                    ADD [LockoutEndUtc] DATETIME2 NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AppUsers_Email'
                      AND object_id = OBJECT_ID(N'[AppUsers]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_AppUsers_Email]
                    ON [AppUsers] ([Email]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AppUsers_UserName'
                      AND object_id = OBJECT_ID(N'[AppUsers]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_AppUsers_UserName]
                    ON [AppUsers] ([UserName]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureBootstrapAdminUserAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.AppUsers.AnyAsync(cancellationToken))
        {
            return;
        }

        var userName = configuration["Security:BootstrapAdmin:UserName"] ?? "admin";
        var email = configuration["Security:BootstrapAdmin:Email"] ?? "admin@local";
        var givenName = configuration["Security:BootstrapAdmin:GivenName"] ?? "System";
        var lastName = configuration["Security:BootstrapAdmin:LastName"] ?? "Administrator";
        var password = configuration["Security:BootstrapAdmin:Password"] ?? "ChangeMeNow!123";
        var nowUtc = DateTime.UtcNow;

        dbContext.AppUsers.Add(new AppUser
        {
            GivenName = givenName,
            LastName = lastName,
            Email = email,
            UserName = userName,
            PasswordHash = PasswordHashService.HashPassword(password),
            RoleName = AppRoles.Admin,
            FailedLoginCount = 0,
            LockoutEndUtc = null,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            PasswordChangedUtc = nowUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSqliteUserColumnsAsync(CancellationToken cancellationToken)
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
            command.CommandText = "PRAGMA table_info('AppUsers')";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }

            if (!columns.Contains("FailedLoginCount"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AppUsers ADD COLUMN FailedLoginCount INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!columns.Contains("LockoutEndUtc"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AppUsers ADD COLUMN LockoutEndUtc TEXT NULL",
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

    private async Task EnsureRoleNormalizationAsync(CancellationToken cancellationToken)
    {
        var users = await dbContext.AppUsers.ToListAsync(cancellationToken);
        var updated = false;

        foreach (var user in users)
        {
            var normalizedRole = AppRoles.Normalize(user.RoleName);
            if (string.IsNullOrWhiteSpace(normalizedRole) || string.Equals(user.RoleName, normalizedRole, StringComparison.Ordinal))
            {
                continue;
            }

            user.RoleName = normalizedRole;
            user.UpdatedUtc = DateTime.UtcNow;
            updated = true;
        }

        if (updated)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureDefaultAppSettingsAsync(CancellationToken cancellationToken)
    {
        var hasDisplayTimeZone = await dbContext.AppSettings
            .AsNoTracking()
            .AnyAsync(x => x.Key == AppSettingKeys.DisplayTimeZone, cancellationToken);

        if (hasDisplayTimeZone)
        {
            return;
        }

        dbContext.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DisplayTimeZone,
            Value = AppSettingDefaults.DisplayTimeZone,
            UpdatedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultConfigurableFieldOptionsAsync(CancellationToken cancellationToken)
    {
        var existingLifecycleStatuses = await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == ConfigurableFieldNames.LifecycleStatus)
            .Select(x => x.Value)
            .ToListAsync(cancellationToken);

        var missingLifecycleStatuses = ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus)
            .Where(value => existingLifecycleStatuses.TrueForAll(existing => !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            .Select((value, index) => new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = value,
                SortOrder = existingLifecycleStatuses.Count + index + 1,
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

    private async Task EnsureSqliteConfigurableFieldOptionColumnsAsync(CancellationToken cancellationToken)
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
            command.CommandText = "PRAGMA table_info('ConfigurableFieldOptions')";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }

            if (!columns.Contains("SortOrder"))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ConfigurableFieldOptions ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0",
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

    private async Task<bool> SqliteColumnExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{tableName}')";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task NormalizeConfigurableFieldOptionSortOrdersAsync(CancellationToken cancellationToken)
    {
        var optionsByField = await dbContext.ConfigurableFieldOptions
            .OrderBy(x => x.FieldName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var hasChanges = false;
        foreach (var fieldGroup in optionsByField.GroupBy(x => x.FieldName, StringComparer.OrdinalIgnoreCase))
        {
            var orderedOptions = fieldGroup
                .OrderBy(x => x.SortOrder <= 0 ? int.MaxValue : x.SortOrder)
                .ThenBy(x => x.CreatedUtc)
                .ThenBy(x => x.Id)
                .ToList();

            for (var index = 0; index < orderedOptions.Count; index++)
            {
                var expectedSortOrder = index + 1;
                if (orderedOptions[index].SortOrder == expectedSortOrder)
                {
                    continue;
                }

                orderedOptions[index].SortOrder = expectedSortOrder;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
