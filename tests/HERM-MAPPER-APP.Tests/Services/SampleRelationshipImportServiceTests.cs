using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class SampleRelationshipImportServiceTests
{
    [Fact]
    public async Task VerifyAsyncShowsPreviewActionsForEachRow()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedHierarchyAsync("Cybersecurity", "Capability A", "Monitoring & Alerting", "TC002");

        var existingProduct = new ProductCatalogItem
        {
            Name = "Graylog"
        };

        fixture.DbContext.ProductCatalogItems.Add(existingProduct);
        await fixture.DbContext.SaveChangesAsync();

        var csvPath = fixture.WriteCsv(
            """
            MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT
            HERM;TD001 Cybersecurity;TCAP001 Capability A;TC002 Monitoring & Alerting;Graylog
            HERM;TD999 Infrastructure;TCAP001 Capability A;TC002 Monitoring & Alerting;Azure Log Analytics
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var verification = await service.VerifyAsync(csvPath);

        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.RowsRead);
        Assert.Equal(1, verification.ProductsMatched);
        Assert.Equal(1, verification.ProductsToAdd);
        Assert.Equal(1, verification.MappingsToAdd);
        Assert.Equal(1, verification.ProductsOnlyRows);
        Assert.Collection(
            verification.Rows,
            row =>
            {
                Assert.Equal("Add Mapping", row.Status);
                Assert.True(row.WillCreateMapping);
                Assert.False(row.WillCreateProduct);
            },
            row =>
            {
                Assert.Equal("Add Product Only", row.Status);
                Assert.False(row.WillCreateMapping);
                Assert.True(row.WillCreateProduct);
            });
    }

    [Fact]
    public async Task VerifyAsyncFailsWhenHeaderIsInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var csvPath = fixture.WriteCsv(
            """
            Domain;Capability;Component;Product
            HERM;TD001 Cybersecurity;TCAP001 Capability A;TC002 Monitoring & Alerting;Graylog
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var verification = await service.VerifyAsync(csvPath);

        Assert.False(verification.IsValid);
        Assert.Contains("MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT", verification.Errors.Single());
    }

    [Fact]
    public async Task ImportAsyncCreatesProductAndMappingWhenHierarchyMatches()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedHierarchyAsync("Cybersecurity", "Capability A", "Monitoring & Alerting", "TC002");
        var csvPath = fixture.WriteCsv(
            """
            MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT
            HERM;TD001 Cybersecurity;TCAP001 Capability A;TC002 Monitoring & Alerting;Graylog
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var summary = await service.ImportAsync(csvPath);

        var products = await fixture.DbContext.ProductCatalogItems.AsNoTracking().ToListAsync();
        var mappings = await fixture.DbContext.ProductMappings.AsNoTracking().ToListAsync();

        Assert.Equal(1, summary.ProductsAdded);
        Assert.Equal(1, summary.MappingsAdded);
        Assert.Single(products);
        Assert.Equal("Graylog", products[0].Name);
        Assert.Single(mappings);
        Assert.NotNull(mappings[0].TrmDomainId);
        Assert.NotNull(mappings[0].TrmCapabilityId);
        Assert.NotNull(mappings[0].TrmComponentId);
    }

    [Fact]
    public async Task ImportAsyncCreatesOnlyProductWhenHierarchyDoesNotMatch()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedHierarchyAsync("Infrastructure", "Capability A", "Monitoring & Alerting", "TC002");
        var csvPath = fixture.WriteCsv(
            """
            MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT
            HERM;TD001 Cybersecurity;TCAP001 Capability B;TC002 Monitoring & Alerting;Graylog
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var summary = await service.ImportAsync(csvPath);

        var products = await fixture.DbContext.ProductCatalogItems.AsNoTracking().ToListAsync();
        var mappings = await fixture.DbContext.ProductMappings.AsNoTracking().ToListAsync();

        Assert.Equal(1, summary.ProductsAdded);
        Assert.Equal(1, summary.ProductsOnlyRows);
        Assert.Equal(0, summary.MappingsAdded);
        Assert.Single(products);
        Assert.Equal("Graylog", products[0].Name);
        Assert.Empty(mappings);
    }

    [Fact]
    public async Task ImportAsyncCreatesOnlyProductWhenDomainDoesNotMatchCapabilityParentDomain()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedHierarchyAsync("Cybersecurity", "Capability A", "Monitoring & Alerting", "TC002");
        var csvPath = fixture.WriteCsv(
            """
            MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT
            HERM;TD999 Infrastructure;TCAP001 Capability A;TC002 Monitoring & Alerting;Graylog
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var summary = await service.ImportAsync(csvPath);

        var products = await fixture.DbContext.ProductCatalogItems.AsNoTracking().ToListAsync();
        var mappings = await fixture.DbContext.ProductMappings.AsNoTracking().ToListAsync();

        Assert.Equal(1, summary.ProductsAdded);
        Assert.Equal(1, summary.ProductsOnlyRows);
        Assert.Equal(0, summary.MappingsAdded);
        Assert.Single(products);
        Assert.Empty(mappings);
    }

    [Fact]
    public async Task ImportAsyncImportsIntoExistingCatalogueAndSkipsDuplicateMapping()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var hierarchy = await fixture.SeedHierarchyAsync("Cybersecurity", "Capability A", "Monitoring & Alerting", "TC002");

        var existingProduct = new ProductCatalogItem
        {
            Name = "Graylog"
        };

        fixture.DbContext.ProductCatalogItems.Add(existingProduct);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ProductMappings.Add(new ProductMapping
        {
            ProductCatalogItemId = existingProduct.Id,
            TrmDomainId = hierarchy.Domain.Id,
            TrmCapabilityId = hierarchy.Capability.Id,
            TrmComponentId = hierarchy.Component.Id,
            MappingStatus = MappingStatus.Complete
        });
        await fixture.DbContext.SaveChangesAsync();

        var csvPath = fixture.WriteCsv(
            """
            MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT
            HERM;TD001 Cybersecurity;TCAP001 Capability A;TC002 Monitoring & Alerting;Graylog
            HERM;TD001 Cybersecurity;TCAP001 Capability A;TC002 Monitoring & Alerting;Azure Log Analytics
            """);

        var service = new SampleRelationshipImportService(fixture.DbContext);

        var summary = await service.ImportAsync(csvPath);

        var products = await fixture.DbContext.ProductCatalogItems.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var mappings = await fixture.DbContext.ProductMappings.AsNoTracking().ToListAsync();

        Assert.Equal(1, summary.ProductsAdded);
        Assert.Equal(1, summary.ProductsMatched);
        Assert.Equal(1, summary.MappingsAdded);
        Assert.Equal(1, summary.MappingsSkippedAsDuplicate);
        Assert.Equal(2, products.Count);
        Assert.Equal(2, mappings.Count);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly TemporaryDirectory tempDirectory;

        private TestFixture(SqliteConnection connection, TemporaryDirectory tempDirectory, AppDbContext dbContext)
        {
            this.connection = connection;
            this.tempDirectory = tempDirectory;
            DbContext = dbContext;
        }

        public AppDbContext DbContext { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var tempDirectory = new TemporaryDirectory();

            return new TestFixture(connection, tempDirectory, dbContext);
        }

        public async Task<(TrmDomain Domain, TrmCapability Capability, TrmComponent Component)> SeedHierarchyAsync(string domainName, string capabilityName, string componentName, string componentCode)
        {
            var domain = new TrmDomain
            {
                Code = "TD001",
                Name = domainName
            };

            var capability = new TrmCapability
            {
                Code = "TCAP001",
                Name = capabilityName,
                ParentDomain = domain
            };

            var component = new TrmComponent
            {
                Code = componentCode,
                Name = componentName,
                ParentCapability = capability
            };

            DbContext.TrmDomains.Add(domain);
            DbContext.TrmCapabilities.Add(capability);
            DbContext.TrmComponents.Add(component);
            await DbContext.SaveChangesAsync();
            return (domain, capability, component);
        }

        public string WriteCsv(string contents)
        {
            var path = Path.Combine(tempDirectory.Path, $"{Guid.NewGuid():N}.csv");
            File.WriteAllText(path, contents.ReplaceLineEndings(Environment.NewLine));
            return path;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
            tempDirectory.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-import-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
