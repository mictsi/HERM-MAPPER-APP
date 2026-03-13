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
        await EnsureServiceTablesAsync(cancellationToken);
        await EnsureProductOwnerTableAsync(cancellationToken);
        await EnsureAppSettingsTableAsync(cancellationToken);
        await EnsureConfigurableFieldOptionsTableAsync(cancellationToken);
        await EnsureDefaultAppSettingsAsync(cancellationToken);
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
            .Where(value => existingLifecycleStatuses.All(existing => !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
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
