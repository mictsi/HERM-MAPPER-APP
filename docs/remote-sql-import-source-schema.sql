/*
    Minimal source schema reference for the remote SQL Server import feature.

    The importer only validates that these tables and columns exist in one schema.
    Replace [herm] with your preferred schema name if needed.
*/

IF SCHEMA_ID(N'herm') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [herm];');
END;
GO

CREATE TABLE [herm].[ProductCatalogItems]
(
    [Id] INT NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Vendor] NVARCHAR(120) NULL,
    [Version] NVARCHAR(80) NULL,
    [LifecycleStatus] NVARCHAR(80) NULL,
    [Description] NVARCHAR(2000) NULL,
    [Notes] NVARCHAR(4000) NULL,
    [IsDeleted] BIT NOT NULL,
    [CreatedUtc] DATETIME2 NULL,
    [UpdatedUtc] DATETIME2 NULL
);
GO

CREATE TABLE [herm].[ProductMappings]
(
    [Id] INT NOT NULL,
    [ProductCatalogItemId] INT NOT NULL,
    [TrmDomainId] INT NULL,
    [TrmCapabilityId] INT NULL,
    [TrmComponentId] INT NULL,
    [MappingStatus] INT NULL,
    [MappingRationale] NVARCHAR(4000) NULL,
    [LastReviewedUtc] DATETIME2 NULL,
    [CreatedUtc] DATETIME2 NULL,
    [UpdatedUtc] DATETIME2 NULL
);
GO

CREATE TABLE [herm].[TrmDomains]
(
    [Id] INT NOT NULL,
    [Code] NVARCHAR(16) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [SourceTitle] NVARCHAR(200) NULL
);
GO

CREATE TABLE [herm].[TrmCapabilities]
(
    [Id] INT NOT NULL,
    [Code] NVARCHAR(16) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [SourceTitle] NVARCHAR(200) NULL,
    [ParentDomainId] INT NULL
);
GO

CREATE TABLE [herm].[TrmComponents]
(
    [Id] INT NOT NULL,
    [Code] NVARCHAR(32) NOT NULL,
    [TechnologyComponentCode] NVARCHAR(32) NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [SourceTitle] NVARCHAR(200) NULL,
    [ParentCapabilityId] INT NULL,
    [IsDeleted] BIT NOT NULL
);
GO

/*
    Optional table: include this only if you want product owners imported.
*/
CREATE TABLE [herm].[ProductCatalogItemOwners]
(
    [ProductCatalogItemId] INT NOT NULL,
    [OwnerValue] NVARCHAR(120) NOT NULL
);
GO
