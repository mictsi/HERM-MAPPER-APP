using System.Diagnostics.CodeAnalysis;
using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
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
    ILogger<DatabaseInitializer> logger,
    ApplicationLookupCache? lookupCache = null)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureServiceTablesAsync(cancellationToken);
        await EnsureApplicationTablesAsync(cancellationToken);
        await EnsureBrmModelTablesAsync(cancellationToken);
        await EnsureBusinessCapabilityTablesAsync(cancellationToken);
        await EnsureLegacyBusinessCapabilitiesHaveBrmModelAsync(cancellationToken);
        await EnsureProductOwnerTableAsync(cancellationToken);
        await EnsureProductSoftDeleteColumnsAsync(cancellationToken);
        await EnsureServiceSoftDeleteColumnsAsync(cancellationToken);
        await EnsureApplicationSoftDeleteColumnsAsync(cancellationToken);
        await EnsureBrmModelSoftDeleteColumnsAsync(cancellationToken);
        await EnsureServiceAssetCriticalityScoreColumnAsync(cancellationToken);
        await EnsureServiceConnectionLayoutColumnAsync(cancellationToken);
        await EnsureAppSettingsTableAsync(cancellationToken);
        await EnsureAiProviderTablesAsync(cancellationToken);
        await EnsureAiUsageLogTableAsync(cancellationToken);
        await EnsureUsersTableAsync(cancellationToken);
        await EnsureAuditLogUserColumnAsync(cancellationToken);
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

        if (!await dbContext.TrmDomains
                .ForReferenceModel(ReferenceModelKind.Trm)
                .AnyAsync(cancellationToken))
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

            await workbookImportService.ImportAsync(workbookPath, cancellationToken: cancellationToken);
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

    private async Task EnsureBrmModelTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "BrmModels" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_BrmModels" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "Area" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "Status" TEXT NOT NULL,
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
                CREATE INDEX IF NOT EXISTS "IX_BrmModels_Name"
                ON "BrmModels" ("Name")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[BrmModels]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BrmModels] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BrmModels] PRIMARY KEY,
                        [Name] NVARCHAR(200) NOT NULL,
                        [Area] NVARCHAR(120) NOT NULL,
                        [Description] NVARCHAR(2000) NULL,
                        [Status] NVARCHAR(80) NOT NULL,
                        [IsDeleted] BIT NOT NULL CONSTRAINT [DF_BrmModels_IsDeleted] DEFAULT 0,
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
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BrmModels_Name'
                      AND object_id = OBJECT_ID(N'[BrmModels]')
                )
                BEGIN
                    CREATE INDEX [IX_BrmModels_Name]
                    ON [BrmModels] ([Name]);
                END
                """,
                cancellationToken);
        }
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "AppDbContext owns the relational connection lifetime.")]
    private async Task EnsureSqliteSchemaUpToDateAsync(CancellationToken cancellationToken)
    {
        var shouldClose = dbContext.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            var columns = await GetSqliteTableColumnsAsync(dbContext.Database.GetDbConnection(), "TrmComponents", cancellationToken);

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

            columns = await GetSqliteTableColumnsAsync(dbContext.Database.GetDbConnection(), "ArmComponents", cancellationToken);

            if (columns.Count > 0)
            {
                if (!columns.Contains("IsDeleted"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE ArmComponents ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                        cancellationToken);
                }

                if (!columns.Contains("DeletedUtc"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE ArmComponents ADD COLUMN DeletedUtc TEXT NULL",
                        cancellationToken);
                }

                if (!columns.Contains("DeletedReason"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE ArmComponents ADD COLUMN DeletedReason TEXT NULL",
                        cancellationToken);
                }
            }

            columns = await GetSqliteTableColumnsAsync(dbContext.Database.GetDbConnection(), "BrmComponents", cancellationToken);

            if (columns.Count > 0)
            {
                if (!columns.Contains("IsDeleted"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE BrmComponents ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                        cancellationToken);
                }

                if (!columns.Contains("DeletedUtc"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE BrmComponents ADD COLUMN DeletedUtc TEXT NULL",
                        cancellationToken);
                }

                if (!columns.Contains("DeletedReason"))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE BrmComponents ADD COLUMN DeletedReason TEXT NULL",
                        cancellationToken);
                }
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "TrmComponentCapabilityLinks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrmComponentCapabilityLinks" PRIMARY KEY AUTOINCREMENT,
                    "TrmComponentId" INTEGER NOT NULL,
                    "TrmCapabilityId" INTEGER NOT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_TrmComponentCapabilityLinks_TrmCapabilities_TrmCapabilityId" FOREIGN KEY ("TrmCapabilityId") REFERENCES "TrmCapabilities" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_TrmComponentCapabilityLinks_TrmComponents_TrmComponentId" FOREIGN KEY ("TrmComponentId") REFERENCES "TrmComponents" ("Id") ON DELETE NO ACTION
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
                    "ActorUserName" TEXT NULL,
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

            await EnsureArmSqliteTablesAsync(cancellationToken);
            await EnsureBrmSqliteTablesAsync(cancellationToken);
            await MigrateLegacyArmRowsAsync(cancellationToken);
            await MigrateLegacyBrmRowsAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<HashSet<string>> GetSqliteTableColumnsAsync(
        System.Data.Common.DbConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}')";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
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
                                        EXEC(N'
                                                INSERT INTO [ProductCatalogItemOwners] ([ProductCatalogItemId], [OwnerValue])
                                                SELECT p.[Id], LTRIM(RTRIM(p.[Owner]))
                                                FROM [ProductCatalogItems] p
                                                WHERE p.[Owner] IS NOT NULL
                                                    AND LTRIM(RTRIM(p.[Owner])) <> N''''
                                                    AND NOT EXISTS (
                                                            SELECT 1
                                                            FROM [ProductCatalogItemOwners] o
                                                            WHERE o.[ProductCatalogItemId] = p.[Id]
                                                                AND LOWER(o.[OwnerValue]) = LOWER(LTRIM(RTRIM(p.[Owner])))
                                                    );');
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
                    "AssetCriticalityScore" INTEGER NOT NULL DEFAULT 1,
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
                            FOREIGN KEY ("FromProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE NO ACTION,
                    CONSTRAINT "FK_ServiceCatalogItemConnections_ProductCatalogItems_ToProductCatalogItemId"
                            FOREIGN KEY ("ToProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE NO ACTION
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
                        [AssetCriticalityScore] INT NOT NULL CONSTRAINT [DF_ServiceCatalogItems_AssetCriticalityScore] DEFAULT 1,
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
                            FOREIGN KEY ([FromProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_ServiceCatalogItemConnections_ProductCatalogItems_ToProductCatalogItemId]
                            FOREIGN KEY ([ToProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE NO ACTION
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

    private async Task EnsureApplicationTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ApplicationCatalogItems" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ApplicationCatalogItems" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "Notes" TEXT NULL,
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
                CREATE TABLE IF NOT EXISTS "ApplicationCatalogItemMappings" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ApplicationCatalogItemMappings" PRIMARY KEY AUTOINCREMENT,
                    "ApplicationCatalogItemId" INTEGER NOT NULL,
                    "ArmComponentId" INTEGER NOT NULL,
                    "ProductMappingId" INTEGER NULL,
                    "ProductCatalogItemId" INTEGER NOT NULL,
                    "IsPrimary" INTEGER NOT NULL DEFAULT 0,
                    "Notes" TEXT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_ApplicationCatalogItemMappings_ApplicationCatalogItems_ApplicationCatalogItemId"
                        FOREIGN KEY ("ApplicationCatalogItemId") REFERENCES "ApplicationCatalogItems" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ApplicationCatalogItemMappings_ArmComponents_ArmComponentId"
                        FOREIGN KEY ("ArmComponentId") REFERENCES "ArmComponents" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ApplicationCatalogItemMappings_ProductMappings_ProductMappingId"
                        FOREIGN KEY ("ProductMappingId") REFERENCES "ProductMappings" ("Id") ON DELETE NO ACTION,
                    CONSTRAINT "FK_ApplicationCatalogItemMappings_ProductCatalogItems_ProductCatalogItemId"
                        FOREIGN KEY ("ProductCatalogItemId") REFERENCES "ProductCatalogItems" ("Id") ON DELETE CASCADE
                )
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("ApplicationCatalogItemMappings", "ProductMappingId", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ApplicationCatalogItemMappings ADD COLUMN ProductMappingId INTEGER NULL",
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE "ApplicationCatalogItemMappings"
                SET "ProductMappingId" = (
                    SELECT MIN(pm."Id")
                    FROM "ProductMappings" pm
                    WHERE pm."ProductCatalogItemId" = "ApplicationCatalogItemMappings"."ProductCatalogItemId"
                    GROUP BY pm."ProductCatalogItemId"
                    HAVING COUNT(*) = 1
                )
                WHERE "ProductMappingId" IS NULL
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DROP INDEX IF EXISTS "IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductCatalogItemId"
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductMappingId"
                ON "ApplicationCatalogItemMappings" ("ApplicationCatalogItemId", "ArmComponentId", "ProductMappingId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ApplicationCatalogItemMappings_ArmComponentId"
                ON "ApplicationCatalogItemMappings" ("ArmComponentId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ApplicationCatalogItemMappings_ProductCatalogItemId"
                ON "ApplicationCatalogItemMappings" ("ProductCatalogItemId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_ApplicationCatalogItemMappings_ProductMappingId"
                ON "ApplicationCatalogItemMappings" ("ProductMappingId")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[ApplicationCatalogItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ApplicationCatalogItems] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ApplicationCatalogItems] PRIMARY KEY,
                        [Name] NVARCHAR(200) NOT NULL,
                        [Description] NVARCHAR(2000) NULL,
                        [Notes] NVARCHAR(4000) NULL,
                        [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ApplicationCatalogItems_IsDeleted] DEFAULT 0,
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
                IF OBJECT_ID(N'[ApplicationCatalogItemMappings]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [ApplicationCatalogItemMappings] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_ApplicationCatalogItemMappings] PRIMARY KEY,
                        [ApplicationCatalogItemId] INT NOT NULL,
                        [ArmComponentId] INT NOT NULL,
                        [ProductMappingId] INT NULL,
                        [ProductCatalogItemId] INT NOT NULL,
                        [IsPrimary] BIT NOT NULL CONSTRAINT [DF_ApplicationCatalogItemMappings_IsPrimary] DEFAULT 0,
                        [Notes] NVARCHAR(1000) NULL,
                        [CreatedUtc] DATETIME2 NOT NULL,
                        CONSTRAINT [FK_ApplicationCatalogItemMappings_ApplicationCatalogItems_ApplicationCatalogItemId]
                            FOREIGN KEY ([ApplicationCatalogItemId]) REFERENCES [ApplicationCatalogItems] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ApplicationCatalogItemMappings_ArmComponents_ArmComponentId]
                            FOREIGN KEY ([ArmComponentId]) REFERENCES [ArmComponents] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ApplicationCatalogItemMappings_ProductMappings_ProductMappingId]
                            FOREIGN KEY ([ProductMappingId]) REFERENCES [ProductMappings] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_ApplicationCatalogItemMappings_ProductCatalogItems_ProductCatalogItemId]
                            FOREIGN KEY ([ProductCatalogItemId]) REFERENCES [ProductCatalogItems] ([Id]) ON DELETE CASCADE
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ApplicationCatalogItemMappings]', N'ProductMappingId') IS NULL
                BEGIN
                    ALTER TABLE [ApplicationCatalogItemMappings]
                    ADD [ProductMappingId] INT NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE name = N'FK_ApplicationCatalogItemMappings_ProductMappings_ProductMappingId'
                )
                BEGIN
                    ALTER TABLE [ApplicationCatalogItemMappings]
                    ADD CONSTRAINT [FK_ApplicationCatalogItemMappings_ProductMappings_ProductMappingId]
                    FOREIGN KEY ([ProductMappingId]) REFERENCES [ProductMappings] ([Id]) ON DELETE NO ACTION;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ;WITH SingleProductMappings AS (
                    SELECT pm.[ProductCatalogItemId], MIN(pm.[Id]) AS [ProductMappingId]
                    FROM [ProductMappings] pm
                    GROUP BY pm.[ProductCatalogItemId]
                    HAVING COUNT(*) = 1
                )
                UPDATE a
                SET [ProductMappingId] = spm.[ProductMappingId]
                FROM [ApplicationCatalogItemMappings] a
                INNER JOIN SingleProductMappings spm ON spm.[ProductCatalogItemId] = a.[ProductCatalogItemId]
                WHERE a.[ProductMappingId] IS NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ApplicationCatalogItemMappings]')
                )
                BEGIN
                    DROP INDEX [IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductCatalogItemId]
                    ON [ApplicationCatalogItemMappings];
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductMappingId'
                      AND object_id = OBJECT_ID(N'[ApplicationCatalogItemMappings]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_ApplicationCatalogItemMappings_ApplicationCatalogItemId_ArmComponentId_ProductMappingId]
                    ON [ApplicationCatalogItemMappings] ([ApplicationCatalogItemId], [ArmComponentId], [ProductMappingId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ApplicationCatalogItemMappings_ArmComponentId'
                      AND object_id = OBJECT_ID(N'[ApplicationCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_ApplicationCatalogItemMappings_ArmComponentId]
                    ON [ApplicationCatalogItemMappings] ([ArmComponentId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ApplicationCatalogItemMappings_ProductCatalogItemId'
                      AND object_id = OBJECT_ID(N'[ApplicationCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_ApplicationCatalogItemMappings_ProductCatalogItemId]
                    ON [ApplicationCatalogItemMappings] ([ProductCatalogItemId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_ApplicationCatalogItemMappings_ProductMappingId'
                      AND object_id = OBJECT_ID(N'[ApplicationCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_ApplicationCatalogItemMappings_ProductMappingId]
                    ON [ApplicationCatalogItemMappings] ([ProductMappingId]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureBusinessCapabilityTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "BusinessCapabilityCatalogItems" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_BusinessCapabilityCatalogItems" PRIMARY KEY AUTOINCREMENT,
                    "BrmModelId" INTEGER NULL,
                    "Name" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "Notes" TEXT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_BusinessCapabilityCatalogItems_BrmModels_BrmModelId"
                        FOREIGN KEY ("BrmModelId") REFERENCES "BrmModels" ("Id") ON DELETE SET NULL
                )
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("BusinessCapabilityCatalogItems", "BrmModelId", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE BusinessCapabilityCatalogItems ADD COLUMN BrmModelId INTEGER NULL",
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_BusinessCapabilityCatalogItems_BrmModelId"
                ON "BusinessCapabilityCatalogItems" ("BrmModelId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "BusinessCapabilityCatalogItemMappings" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_BusinessCapabilityCatalogItemMappings" PRIMARY KEY AUTOINCREMENT,
                    "BusinessCapabilityCatalogItemId" INTEGER NOT NULL,
                    "BrmComponentId" INTEGER NOT NULL,
                    "ArmComponentId" INTEGER NOT NULL,
                    "ArmCapabilityId" INTEGER NULL,
                    "IsPrimary" INTEGER NOT NULL DEFAULT 0,
                    "Notes" TEXT NULL,
                    "CreatedUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItems_BusinessCapabilityCatalogItemId"
                        FOREIGN KEY ("BusinessCapabilityCatalogItemId") REFERENCES "BusinessCapabilityCatalogItems" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_BusinessCapabilityCatalogItemMappings_BrmComponents_BrmComponentId"
                        FOREIGN KEY ("BrmComponentId") REFERENCES "BrmComponents" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_BusinessCapabilityCatalogItemMappings_ArmComponents_ArmComponentId"
                        FOREIGN KEY ("ArmComponentId") REFERENCES "ArmComponents" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_BusinessCapabilityCatalogItemMappings_ArmCapabilities_ArmCapabilityId"
                        FOREIGN KEY ("ArmCapabilityId") REFERENCES "ArmCapabilities" ("Id") ON DELETE NO ACTION
                )
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("BusinessCapabilityCatalogItemMappings", "ArmCapabilityId", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE BusinessCapabilityCatalogItemMappings ADD COLUMN ArmCapabilityId INTEGER NULL",
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE "BusinessCapabilityCatalogItemMappings"
                SET "ArmCapabilityId" = COALESCE(
                    (
                        SELECT MIN(link."ArmCapabilityId")
                        FROM "ArmComponentCapabilityLinks" link
                        WHERE link."ArmComponentId" = "BusinessCapabilityCatalogItemMappings"."ArmComponentId"
                    ),
                    (
                        SELECT component."ParentCapabilityId"
                        FROM "ArmComponents" component
                        WHERE component."Id" = "BusinessCapabilityCatalogItemMappings"."ArmComponentId"
                    )
                )
                WHERE "ArmCapabilityId" IS NULL
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DROP INDEX IF EXISTS "IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId"
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId_ArmCapabilityId"
                ON "BusinessCapabilityCatalogItemMappings" ("BusinessCapabilityCatalogItemId", "BrmComponentId", "ArmComponentId", "ArmCapabilityId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_BusinessCapabilityCatalogItemMappings_BrmComponentId"
                ON "BusinessCapabilityCatalogItemMappings" ("BrmComponentId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_BusinessCapabilityCatalogItemMappings_ArmComponentId"
                ON "BusinessCapabilityCatalogItemMappings" ("ArmComponentId")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_BusinessCapabilityCatalogItemMappings_ArmCapabilityId"
                ON "BusinessCapabilityCatalogItemMappings" ("ArmCapabilityId")
                """,
                cancellationToken);

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[BusinessCapabilityCatalogItems]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BusinessCapabilityCatalogItems] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BusinessCapabilityCatalogItems] PRIMARY KEY,
                        [BrmModelId] INT NULL,
                        [Name] NVARCHAR(200) NOT NULL,
                        [Description] NVARCHAR(2000) NULL,
                        [Notes] NVARCHAR(4000) NULL,
                        [CreatedUtc] DATETIME2 NOT NULL,
                        [UpdatedUtc] DATETIME2 NOT NULL,
                        CONSTRAINT [FK_BusinessCapabilityCatalogItems_BrmModels_BrmModelId]
                            FOREIGN KEY ([BrmModelId]) REFERENCES [BrmModels] ([Id]) ON DELETE SET NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[BusinessCapabilityCatalogItems]', N'BrmModelId') IS NULL
                BEGIN
                    ALTER TABLE [BusinessCapabilityCatalogItems]
                    ADD [BrmModelId] INT NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE name = N'FK_BusinessCapabilityCatalogItems_BrmModels_BrmModelId'
                      AND parent_object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItems]')
                )
                BEGIN
                    ALTER TABLE [BusinessCapabilityCatalogItems]
                    ADD CONSTRAINT [FK_BusinessCapabilityCatalogItems_BrmModels_BrmModelId]
                        FOREIGN KEY ([BrmModelId]) REFERENCES [BrmModels] ([Id]) ON DELETE SET NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItems_BrmModelId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItems]')
                )
                BEGIN
                    CREATE INDEX [IX_BusinessCapabilityCatalogItems_BrmModelId]
                    ON [BusinessCapabilityCatalogItems] ([BrmModelId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BusinessCapabilityCatalogItemMappings] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_BusinessCapabilityCatalogItemMappings] PRIMARY KEY,
                        [BusinessCapabilityCatalogItemId] INT NOT NULL,
                        [BrmComponentId] INT NOT NULL,
                        [ArmComponentId] INT NOT NULL,
                        [ArmCapabilityId] INT NULL,
                        [IsPrimary] BIT NOT NULL CONSTRAINT [DF_BusinessCapabilityCatalogItemMappings_IsPrimary] DEFAULT 0,
                        [Notes] NVARCHAR(1000) NULL,
                        [CreatedUtc] DATETIME2 NOT NULL,
                        CONSTRAINT [FK_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItems_BusinessCapabilityCatalogItemId]
                            FOREIGN KEY ([BusinessCapabilityCatalogItemId]) REFERENCES [BusinessCapabilityCatalogItems] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_BusinessCapabilityCatalogItemMappings_BrmComponents_BrmComponentId]
                            FOREIGN KEY ([BrmComponentId]) REFERENCES [BrmComponents] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_BusinessCapabilityCatalogItemMappings_ArmComponents_ArmComponentId]
                            FOREIGN KEY ([ArmComponentId]) REFERENCES [ArmComponents] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_BusinessCapabilityCatalogItemMappings_ArmCapabilities_ArmCapabilityId]
                            FOREIGN KEY ([ArmCapabilityId]) REFERENCES [ArmCapabilities] ([Id])
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[BusinessCapabilityCatalogItemMappings]', N'ArmCapabilityId') IS NULL
                BEGIN
                    ALTER TABLE [BusinessCapabilityCatalogItemMappings]
                    ADD [ArmCapabilityId] INT NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE mapping
                SET [ArmCapabilityId] = COALESCE(
                    links.[ArmCapabilityId],
                    component.[ParentCapabilityId]
                )
                FROM [BusinessCapabilityCatalogItemMappings] mapping
                OUTER APPLY (
                    SELECT MIN([ArmCapabilityId]) AS [ArmCapabilityId]
                    FROM [ArmComponentCapabilityLinks]
                    WHERE [ArmComponentId] = mapping.[ArmComponentId]
                ) links
                LEFT JOIN [ArmComponents] component ON component.[Id] = mapping.[ArmComponentId]
                WHERE mapping.[ArmCapabilityId] IS NULL;
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE name = N'FK_BusinessCapabilityCatalogItemMappings_ArmCapabilities_ArmCapabilityId'
                      AND parent_object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    ALTER TABLE [BusinessCapabilityCatalogItemMappings]
                    ADD CONSTRAINT [FK_BusinessCapabilityCatalogItemMappings_ArmCapabilities_ArmCapabilityId]
                        FOREIGN KEY ([ArmCapabilityId]) REFERENCES [ArmCapabilities] ([Id]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    DROP INDEX [IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId]
                    ON [BusinessCapabilityCatalogItemMappings];
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId_ArmCapabilityId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_BusinessCapabilityCatalogItemMappings_BusinessCapabilityCatalogItemId_BrmComponentId_ArmComponentId_ArmCapabilityId]
                    ON [BusinessCapabilityCatalogItemMappings] ([BusinessCapabilityCatalogItemId], [BrmComponentId], [ArmComponentId], [ArmCapabilityId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItemMappings_BrmComponentId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_BusinessCapabilityCatalogItemMappings_BrmComponentId]
                    ON [BusinessCapabilityCatalogItemMappings] ([BrmComponentId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItemMappings_ArmComponentId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_BusinessCapabilityCatalogItemMappings_ArmComponentId]
                    ON [BusinessCapabilityCatalogItemMappings] ([ArmComponentId]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_BusinessCapabilityCatalogItemMappings_ArmCapabilityId'
                      AND object_id = OBJECT_ID(N'[BusinessCapabilityCatalogItemMappings]')
                )
                BEGIN
                    CREATE INDEX [IX_BusinessCapabilityCatalogItemMappings_ArmCapabilityId]
                    ON [BusinessCapabilityCatalogItemMappings] ([ArmCapabilityId]);
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureLegacyBusinessCapabilitiesHaveBrmModelAsync(CancellationToken cancellationToken)
    {
        var unassignedCapabilityCount = await dbContext.BusinessCapabilityCatalogItems
            .CountAsync(x => x.BrmModelId == null, cancellationToken);
        if (unassignedCapabilityCount == 0)
        {
            return;
        }

        var fallbackModel = await dbContext.BrmModels
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallbackModel is null)
        {
            fallbackModel = new BrmModel
            {
                Name = "Primary BRM Model",
                Area = "General",
                Description = "Created automatically to group legacy capability records after BRM model support was introduced.",
                Status = "Production",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            dbContext.BrmModels.Add(fallbackModel);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var capabilities = await dbContext.BusinessCapabilityCatalogItems
            .Where(x => x.BrmModelId == null)
            .ToListAsync(cancellationToken);

        foreach (var capability in capabilities)
        {
            capability.BrmModelId = fallbackModel.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAuditLogUserColumnAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("AuditLogEntries", "ActorUserName", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AuditLogEntries ADD COLUMN ActorUserName TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AuditLogEntries]', N'ActorUserName') IS NULL
                BEGIN
                    ALTER TABLE [AuditLogEntries]
                    ADD [ActorUserName] NVARCHAR(200) NULL;
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

    private async Task EnsureApplicationSoftDeleteColumnsAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("ApplicationCatalogItems", "IsDeleted", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ApplicationCatalogItems ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ApplicationCatalogItems", "DeletedUtc", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ApplicationCatalogItems ADD COLUMN DeletedUtc TEXT NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("ApplicationCatalogItems", "DeletedReason", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ApplicationCatalogItems ADD COLUMN DeletedReason TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ApplicationCatalogItems]', N'IsDeleted') IS NULL
                BEGIN
                    ALTER TABLE [ApplicationCatalogItems]
                    ADD [IsDeleted] BIT NOT NULL CONSTRAINT [DF_ApplicationCatalogItems_IsDeleted] DEFAULT 0;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ApplicationCatalogItems]', N'DeletedUtc') IS NULL
                BEGIN
                    ALTER TABLE [ApplicationCatalogItems]
                    ADD [DeletedUtc] DATETIME2 NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ApplicationCatalogItems]', N'DeletedReason') IS NULL
                BEGIN
                    ALTER TABLE [ApplicationCatalogItems]
                    ADD [DeletedReason] NVARCHAR(400) NULL;
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureBrmModelSoftDeleteColumnsAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("BrmModels", "IsDeleted", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE BrmModels ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("BrmModels", "DeletedUtc", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE BrmModels ADD COLUMN DeletedUtc TEXT NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("BrmModels", "DeletedReason", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE BrmModels ADD COLUMN DeletedReason TEXT NULL",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[BrmModels]', N'IsDeleted') IS NULL
                BEGIN
                    ALTER TABLE [BrmModels]
                    ADD [IsDeleted] BIT NOT NULL CONSTRAINT [DF_BrmModels_IsDeleted] DEFAULT 0;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[BrmModels]', N'DeletedUtc') IS NULL
                BEGIN
                    ALTER TABLE [BrmModels]
                    ADD [DeletedUtc] DATETIME2 NULL;
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[BrmModels]', N'DeletedReason') IS NULL
                BEGIN
                    ALTER TABLE [BrmModels]
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

    private async Task EnsureServiceAssetCriticalityScoreColumnAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            if (!await SqliteColumnExistsAsync("ServiceCatalogItems", "AssetCriticalityScore", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE ServiceCatalogItems ADD COLUMN AssetCriticalityScore INTEGER NOT NULL DEFAULT 1",
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[ServiceCatalogItems]', N'AssetCriticalityScore') IS NULL
                BEGIN
                    ALTER TABLE [ServiceCatalogItems]
                    ADD [AssetCriticalityScore] INT NOT NULL CONSTRAINT [DF_ServiceCatalogItems_AssetCriticalityScore] DEFAULT 1;
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
                        [Value] NVARCHAR(4000) NOT NULL,
                        [UpdatedUtc] DATETIME2 NOT NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[AppSettings]')
                      AND name = N'Value'
                      AND max_length < 8000
                )
                BEGIN
                    ALTER TABLE [AppSettings]
                    ALTER COLUMN [Value] NVARCHAR(4000) NOT NULL;
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

    private async Task EnsureAiProviderTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "AiProviderConfigurations" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AiProviderConfigurations" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "ProviderType" INTEGER NOT NULL,
                    "Endpoint" TEXT NOT NULL,
                    "Model" TEXT NOT NULL,
                    "ApiVersion" TEXT NULL,
                    "InputCostPerMillionTokensSek" REAL NULL,
                    "OutputCostPerMillionTokensSek" REAL NULL,
                    "TimeoutSeconds" INTEGER NOT NULL,
                    "IsActive" INTEGER NOT NULL DEFAULT 0,
                    "CreatedUtc" TEXT NOT NULL,
                    "UpdatedUtc" TEXT NOT NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_AiProviderConfigurations_Name"
                ON "AiProviderConfigurations" ("Name")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_AiProviderConfigurations_IsActive"
                ON "AiProviderConfigurations" ("IsActive")
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("AiProviderConfigurations", "InputCostPerMillionTokensSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiProviderConfigurations ADD COLUMN InputCostPerMillionTokensSek REAL NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("AiProviderConfigurations", "OutputCostPerMillionTokensSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiProviderConfigurations ADD COLUMN OutputCostPerMillionTokensSek REAL NULL",
                    cancellationToken);
            }

            if (await SqliteColumnExistsAsync("AiProviderConfigurations", "CostPerMillionTokensSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "AiProviderConfigurations"
                    SET "InputCostPerMillionTokensSek" = COALESCE("InputCostPerMillionTokensSek", "CostPerMillionTokensSek"),
                        "OutputCostPerMillionTokensSek" = COALESCE("OutputCostPerMillionTokensSek", "CostPerMillionTokensSek")
                    WHERE "CostPerMillionTokensSek" IS NOT NULL
                    """,
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[AiProviderConfigurations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AiProviderConfigurations] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AiProviderConfigurations] PRIMARY KEY,
                        [Name] NVARCHAR(120) NOT NULL,
                        [ProviderType] INT NOT NULL,
                        [Endpoint] NVARCHAR(2048) NOT NULL,
                        [Model] NVARCHAR(200) NOT NULL,
                        [ApiVersion] NVARCHAR(80) NULL,
                        [InputCostPerMillionTokensSek] DECIMAL(18,6) NULL,
                        [OutputCostPerMillionTokensSek] DECIMAL(18,6) NULL,
                        [TimeoutSeconds] INT NOT NULL,
                        [IsActive] BIT NOT NULL CONSTRAINT [DF_AiProviderConfigurations_IsActive] DEFAULT 0,
                        [CreatedUtc] DATETIME2 NOT NULL,
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
                    WHERE name = N'IX_AiProviderConfigurations_Name'
                      AND object_id = OBJECT_ID(N'[AiProviderConfigurations]')
                )
                BEGIN
                    CREATE INDEX [IX_AiProviderConfigurations_Name]
                    ON [AiProviderConfigurations] ([Name]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AiProviderConfigurations_IsActive'
                      AND object_id = OBJECT_ID(N'[AiProviderConfigurations]')
                )
                BEGIN
                    CREATE INDEX [IX_AiProviderConfigurations_IsActive]
                    ON [AiProviderConfigurations] ([IsActive]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AiProviderConfigurations]', N'InputCostPerMillionTokensSek') IS NULL
                BEGIN
                    ALTER TABLE [AiProviderConfigurations]
                    ADD [InputCostPerMillionTokensSek] DECIMAL(18,6) NULL;
                END

                IF COL_LENGTH(N'[AiProviderConfigurations]', N'OutputCostPerMillionTokensSek') IS NULL
                BEGIN
                    ALTER TABLE [AiProviderConfigurations]
                    ADD [OutputCostPerMillionTokensSek] DECIMAL(18,6) NULL;
                END

                IF COL_LENGTH(N'[AiProviderConfigurations]', N'CostPerMillionTokensSek') IS NOT NULL
                BEGIN
                    UPDATE [AiProviderConfigurations]
                    SET [InputCostPerMillionTokensSek] = COALESCE([InputCostPerMillionTokensSek], [CostPerMillionTokensSek]),
                        [OutputCostPerMillionTokensSek] = COALESCE([OutputCostPerMillionTokensSek], [CostPerMillionTokensSek])
                    WHERE [CostPerMillionTokensSek] IS NOT NULL;
                END
                """,
                cancellationToken);
        }
    }

    private async Task EnsureAiUsageLogTableAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "AiRequestUsageLogs" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AiRequestUsageLogs" PRIMARY KEY AUTOINCREMENT,
                    "AiProviderConfigurationId" INTEGER NULL,
                    "ProviderName" TEXT NOT NULL,
                    "ProviderType" INTEGER NOT NULL,
                    "Model" TEXT NOT NULL,
                    "RequestKind" TEXT NOT NULL,
                    "RequestSummary" TEXT NOT NULL,
                    "PromptTokens" INTEGER NULL,
                    "CompletionTokens" INTEGER NULL,
                    "TotalTokens" INTEGER NULL,
                    "EstimatedInputCostSek" REAL NULL,
                    "EstimatedOutputCostSek" REAL NULL,
                    "EstimatedTotalCostSek" REAL NULL,
                    "Outcome" INTEGER NOT NULL DEFAULT 2,
                    "WasSuccessful" INTEGER NOT NULL,
                    "DurationMilliseconds" INTEGER NOT NULL,
                    "ErrorMessage" TEXT NULL,
                    "OccurredUtc" TEXT NOT NULL,
                    CONSTRAINT "FK_AiRequestUsageLogs_AiProviderConfigurations_AiProviderConfigurationId"
                        FOREIGN KEY ("AiProviderConfigurationId") REFERENCES "AiProviderConfigurations" ("Id") ON DELETE SET NULL
                )
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_AiRequestUsageLogs_OccurredUtc"
                ON "AiRequestUsageLogs" ("OccurredUtc")
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_AiRequestUsageLogs_AiProviderConfigurationId_OccurredUtc"
                ON "AiRequestUsageLogs" ("AiProviderConfigurationId", "OccurredUtc")
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("AiRequestUsageLogs", "EstimatedInputCostSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiRequestUsageLogs ADD COLUMN EstimatedInputCostSek REAL NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("AiRequestUsageLogs", "EstimatedOutputCostSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiRequestUsageLogs ADD COLUMN EstimatedOutputCostSek REAL NULL",
                    cancellationToken);
            }

            if (!await SqliteColumnExistsAsync("AiRequestUsageLogs", "EstimatedTotalCostSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiRequestUsageLogs ADD COLUMN EstimatedTotalCostSek REAL NULL",
                    cancellationToken);
            }

            if (await SqliteColumnExistsAsync("AiRequestUsageLogs", "EstimatedCostSek", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "AiRequestUsageLogs"
                    SET "EstimatedTotalCostSek" = COALESCE("EstimatedTotalCostSek", "EstimatedCostSek")
                    WHERE "EstimatedCostSek" IS NOT NULL
                    """,
                    cancellationToken);
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE "AiRequestUsageLogs"
                SET "EstimatedTotalCostSek" = COALESCE("EstimatedTotalCostSek", COALESCE("EstimatedInputCostSek", 0) + COALESCE("EstimatedOutputCostSek", 0))
                WHERE "EstimatedTotalCostSek" IS NULL
                  AND ("EstimatedInputCostSek" IS NOT NULL OR "EstimatedOutputCostSek" IS NOT NULL)
                """,
                cancellationToken);

            if (!await SqliteColumnExistsAsync("AiRequestUsageLogs", "Outcome", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE AiRequestUsageLogs ADD COLUMN Outcome INTEGER NOT NULL DEFAULT 2",
                    cancellationToken);

                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "AiRequestUsageLogs"
                    SET "Outcome" = CASE
                        WHEN "WasSuccessful" = 1 THEN 1
                        ELSE 2
                    END
                    """,
                    cancellationToken);
            }

            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[AiRequestUsageLogs]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [AiRequestUsageLogs] (
                        [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AiRequestUsageLogs] PRIMARY KEY,
                        [AiProviderConfigurationId] INT NULL,
                        [ProviderName] NVARCHAR(120) NOT NULL,
                        [ProviderType] INT NOT NULL,
                        [Model] NVARCHAR(200) NOT NULL,
                        [RequestKind] NVARCHAR(80) NOT NULL,
                        [RequestSummary] NVARCHAR(400) NOT NULL,
                        [PromptTokens] INT NULL,
                        [CompletionTokens] INT NULL,
                        [TotalTokens] INT NULL,
                        [EstimatedInputCostSek] DECIMAL(18,6) NULL,
                        [EstimatedOutputCostSek] DECIMAL(18,6) NULL,
                        [EstimatedTotalCostSek] DECIMAL(18,6) NULL,
                        [Outcome] INT NOT NULL CONSTRAINT [DF_AiRequestUsageLogs_Outcome] DEFAULT 2,
                        [WasSuccessful] BIT NOT NULL,
                        [DurationMilliseconds] INT NOT NULL,
                        [ErrorMessage] NVARCHAR(2000) NULL,
                        [OccurredUtc] DATETIME2 NOT NULL,
                        CONSTRAINT [FK_AiRequestUsageLogs_AiProviderConfigurations_AiProviderConfigurationId]
                            FOREIGN KEY ([AiProviderConfigurationId]) REFERENCES [AiProviderConfigurations] ([Id]) ON DELETE SET NULL
                    );
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AiRequestUsageLogs_OccurredUtc'
                      AND object_id = OBJECT_ID(N'[AiRequestUsageLogs]')
                )
                BEGIN
                    CREATE INDEX [IX_AiRequestUsageLogs_OccurredUtc]
                    ON [AiRequestUsageLogs] ([OccurredUtc]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_AiRequestUsageLogs_AiProviderConfigurationId_OccurredUtc'
                      AND object_id = OBJECT_ID(N'[AiRequestUsageLogs]')
                )
                BEGIN
                    CREATE INDEX [IX_AiRequestUsageLogs_AiProviderConfigurationId_OccurredUtc]
                    ON [AiRequestUsageLogs] ([AiProviderConfigurationId], [OccurredUtc]);
                END
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AiRequestUsageLogs]', N'EstimatedInputCostSek') IS NULL
                BEGIN
                    ALTER TABLE [AiRequestUsageLogs]
                    ADD [EstimatedInputCostSek] DECIMAL(18,6) NULL;
                END

                IF COL_LENGTH(N'[AiRequestUsageLogs]', N'EstimatedOutputCostSek') IS NULL
                BEGIN
                    ALTER TABLE [AiRequestUsageLogs]
                    ADD [EstimatedOutputCostSek] DECIMAL(18,6) NULL;
                END

                IF COL_LENGTH(N'[AiRequestUsageLogs]', N'EstimatedTotalCostSek') IS NULL
                BEGIN
                    ALTER TABLE [AiRequestUsageLogs]
                    ADD [EstimatedTotalCostSek] DECIMAL(18,6) NULL;
                END

                IF COL_LENGTH(N'[AiRequestUsageLogs]', N'EstimatedCostSek') IS NOT NULL
                BEGIN
                    UPDATE [AiRequestUsageLogs]
                    SET [EstimatedTotalCostSek] = COALESCE([EstimatedTotalCostSek], [EstimatedCostSek])
                    WHERE [EstimatedCostSek] IS NOT NULL;
                END

                UPDATE [AiRequestUsageLogs]
                SET [EstimatedTotalCostSek] = COALESCE([EstimatedTotalCostSek], COALESCE([EstimatedInputCostSek], 0) + COALESCE([EstimatedOutputCostSek], 0))
                WHERE [EstimatedTotalCostSek] IS NULL
                  AND ([EstimatedInputCostSek] IS NOT NULL OR [EstimatedOutputCostSek] IS NOT NULL);
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'[AiRequestUsageLogs]', N'Outcome') IS NULL
                BEGIN
                    ALTER TABLE [AiRequestUsageLogs]
                    ADD [Outcome] INT NOT NULL CONSTRAINT [DF_AiRequestUsageLogs_Outcome] DEFAULT 2;

                    UPDATE [AiRequestUsageLogs]
                    SET [Outcome] = CASE
                        WHEN [WasSuccessful] = 1 THEN 1
                        ELSE 2
                    END;
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

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "AppDbContext owns the relational connection lifetime.")]
    private async Task EnsureSqliteUserColumnsAsync(CancellationToken cancellationToken)
    {
        var shouldClose = dbContext.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
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
                await dbContext.Database.CloseConnectionAsync();
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
        lookupCache?.InvalidateAppSetting(AppSettingKeys.DisplayTimeZone);
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
        lookupCache?.InvalidateConfigurableFieldOptions(ConfigurableFieldNames.LifecycleStatus);
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "AppDbContext owns the relational connection lifetime.")]
    private async Task EnsureSqliteConfigurableFieldOptionColumnsAsync(CancellationToken cancellationToken)
    {
        var shouldClose = dbContext.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
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
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP004:Don't ignore created IDisposable", Justification = "AppDbContext owns the relational connection lifetime.")]
    private async Task<bool> SqliteColumnExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        var shouldClose = dbContext.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
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
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task EnsureArmSqliteTablesAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ArmDomains" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArmDomains" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArmDomains_Code"
            ON "ArmDomains" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ArmCapabilities" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArmCapabilities" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "ParentDomainCode" TEXT NULL,
                "ParentDomainId" INTEGER NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL,
                CONSTRAINT "FK_ArmCapabilities_ArmDomains_ParentDomainId" FOREIGN KEY ("ParentDomainId") REFERENCES "ArmDomains" ("Id") ON DELETE CASCADE
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArmCapabilities_Code"
            ON "ArmCapabilities" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ArmCapabilities_ParentDomainId"
            ON "ArmCapabilities" ("ParentDomainId")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ArmComponents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArmComponents" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "ParentCapabilityCode" TEXT NULL,
                "ParentCapabilityId" INTEGER NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL,
                "ProductExamples" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "DeletedUtc" TEXT NULL,
                "DeletedReason" TEXT NULL,
                CONSTRAINT "FK_ArmComponents_ArmCapabilities_ParentCapabilityId" FOREIGN KEY ("ParentCapabilityId") REFERENCES "ArmCapabilities" ("Id") ON DELETE CASCADE
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArmComponents_Code"
            ON "ArmComponents" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_ArmComponents_ParentCapabilityId"
            ON "ArmComponents" ("ParentCapabilityId")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ArmComponentCapabilityLinks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArmComponentCapabilityLinks" PRIMARY KEY AUTOINCREMENT,
                "ArmComponentId" INTEGER NOT NULL,
                "ArmCapabilityId" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                CONSTRAINT "FK_ArmComponentCapabilityLinks_ArmCapabilities_ArmCapabilityId" FOREIGN KEY ("ArmCapabilityId") REFERENCES "ArmCapabilities" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ArmComponentCapabilityLinks_ArmComponents_ArmComponentId" FOREIGN KEY ("ArmComponentId") REFERENCES "ArmComponents" ("Id") ON DELETE NO ACTION
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArmComponentCapabilityLinks_ArmComponentId_ArmCapabilityId"
            ON "ArmComponentCapabilityLinks" ("ArmComponentId", "ArmCapabilityId")
            """,
            cancellationToken);
    }

    private async Task EnsureBrmSqliteTablesAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BrmDomains" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BrmDomains" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BrmDomains_Code"
            ON "BrmDomains" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BrmCapabilities" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BrmCapabilities" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "ParentDomainCode" TEXT NULL,
                "ParentDomainId" INTEGER NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL,
                CONSTRAINT "FK_BrmCapabilities_BrmDomains_ParentDomainId" FOREIGN KEY ("ParentDomainId") REFERENCES "BrmDomains" ("Id") ON DELETE CASCADE
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BrmCapabilities_Code"
            ON "BrmCapabilities" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BrmCapabilities_ParentDomainId"
            ON "BrmCapabilities" ("ParentDomainId")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BrmComponents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BrmComponents" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SourceTitle" TEXT NULL,
                "ParentCapabilityCode" TEXT NULL,
                "ParentCapabilityId" INTEGER NULL,
                "Description" TEXT NULL,
                "Comments" TEXT NULL,
                "ProductExamples" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "DeletedUtc" TEXT NULL,
                "DeletedReason" TEXT NULL,
                CONSTRAINT "FK_BrmComponents_BrmCapabilities_ParentCapabilityId" FOREIGN KEY ("ParentCapabilityId") REFERENCES "BrmCapabilities" ("Id") ON DELETE CASCADE
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BrmComponents_Code"
            ON "BrmComponents" ("Code")
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BrmComponents_ParentCapabilityId"
            ON "BrmComponents" ("ParentCapabilityId")
            """,
            cancellationToken);
    }

    private async Task MigrateLegacyArmRowsAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ArmDomains" ("Code", "Name", "SourceTitle", "Description", "Comments")
            SELECT d."Code", d."Name", d."SourceTitle", d."Description", d."Comments"
            FROM "TrmDomains" d
            WHERE d."Code" LIKE 'AD%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "ArmDomains" x
                  WHERE x."Code" = d."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ArmCapabilities" ("Code", "Name", "SourceTitle", "ParentDomainCode", "ParentDomainId", "Description", "Comments")
            SELECT c."Code",
                   c."Name",
                   c."SourceTitle",
                   c."ParentDomainCode",
                   d."Id",
                   c."Description",
                   c."Comments"
            FROM "TrmCapabilities" c
            LEFT JOIN "ArmDomains" d ON d."Code" = c."ParentDomainCode"
            WHERE c."Code" LIKE 'AP%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "ArmCapabilities" x
                  WHERE x."Code" = c."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE "ArmCapabilities"
            SET "ParentDomainId" = (
                SELECT d."Id"
                FROM "ArmDomains" d
                WHERE d."Code" = "ArmCapabilities"."ParentDomainCode"
            )
            WHERE "ParentDomainCode" IS NOT NULL
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ArmComponents" ("Code", "Name", "SourceTitle", "ParentCapabilityCode", "ParentCapabilityId", "Description", "Comments", "ProductExamples")
            SELECT c."Code",
                   c."Name",
                   c."SourceTitle",
                   c."ParentCapabilityCode",
                   p."Id",
                   c."Description",
                   c."Comments",
                   c."ProductExamples"
            FROM "TrmComponents" c
            LEFT JOIN "ArmCapabilities" p ON p."Code" = c."ParentCapabilityCode"
            WHERE c."Code" LIKE 'AC%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "ArmComponents" x
                  WHERE x."Code" = c."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE "ArmComponents"
            SET "ParentCapabilityId" = (
                SELECT c."Id"
                FROM "ArmCapabilities" c
                WHERE c."Code" = "ArmComponents"."ParentCapabilityCode"
            )
            WHERE "ParentCapabilityCode" IS NOT NULL
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ArmComponentCapabilityLinks" ("ArmComponentId", "ArmCapabilityId", "CreatedUtc")
            SELECT ac."Id",
                   ap."Id",
                   COALESCE(tl."CreatedUtc", CURRENT_TIMESTAMP)
            FROM "TrmComponentCapabilityLinks" tl
            INNER JOIN "TrmComponents" tc ON tc."Id" = tl."TrmComponentId"
            INNER JOIN "TrmCapabilities" tp ON tp."Id" = tl."TrmCapabilityId"
            INNER JOIN "ArmComponents" ac ON ac."Code" = tc."Code"
            INNER JOIN "ArmCapabilities" ap ON ap."Code" = tp."Code"
            WHERE tc."Code" LIKE 'AC%'
              AND tp."Code" LIKE 'AP%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "ArmComponentCapabilityLinks" x
                  WHERE x."ArmComponentId" = ac."Id"
                    AND x."ArmCapabilityId" = ap."Id"
              )
            """,
            cancellationToken);
    }

    private async Task MigrateLegacyBrmRowsAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BrmDomains" ("Code", "Name", "SourceTitle", "Description", "Comments")
            SELECT d."Code", d."Name", d."SourceTitle", d."Description", d."Comments"
            FROM "TrmDomains" d
            WHERE d."Code" LIKE 'BD%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "BrmDomains" x
                  WHERE x."Code" = d."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BrmCapabilities" ("Code", "Name", "SourceTitle", "ParentDomainCode", "ParentDomainId", "Description", "Comments")
            SELECT c."Code",
                   c."Name",
                   c."SourceTitle",
                   c."ParentDomainCode",
                   d."Id",
                   c."Description",
                   c."Comments"
            FROM "TrmCapabilities" c
            LEFT JOIN "BrmDomains" d ON d."Code" = c."ParentDomainCode"
            WHERE c."Code" LIKE 'BC%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "BrmCapabilities" x
                  WHERE x."Code" = c."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE "BrmCapabilities"
            SET "ParentDomainId" = (
                SELECT d."Id"
                FROM "BrmDomains" d
                WHERE d."Code" = "BrmCapabilities"."ParentDomainCode"
            )
            WHERE "ParentDomainCode" IS NOT NULL
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "BrmComponents" ("Code", "Name", "SourceTitle", "ParentCapabilityCode", "ParentCapabilityId", "Description", "Comments", "ProductExamples")
            SELECT c."Code",
                   c."Name",
                   c."SourceTitle",
                   c."ParentCapabilityCode",
                   p."Id",
                   c."Description",
                   c."Comments",
                   c."ProductExamples"
            FROM "TrmComponents" c
            LEFT JOIN "BrmCapabilities" p ON p."Code" = c."ParentCapabilityCode"
            WHERE c."Code" LIKE 'BC%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM "BrmComponents" x
                  WHERE x."Code" = c."Code"
              )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE "BrmComponents"
            SET "ParentCapabilityId" = (
                SELECT c."Id"
                FROM "BrmCapabilities" c
                WHERE c."Code" = "BrmComponents"."ParentCapabilityCode"
            )
            WHERE "ParentCapabilityCode" IS NOT NULL
            """,
            cancellationToken);
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
