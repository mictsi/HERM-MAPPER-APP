using System.IO.Compression;
using System.Xml.Linq;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class TrmWorkbookImportServiceTests
{
    [Fact]
    public async Task VerifyAsyncReturnsErrorWhenRequiredSheetIsMissingAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "TRM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "TD001", "Technology", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "TRM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "TP001", "Observability", "TD001 Technology", "Capability description", "Capability comments"]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath, ReferenceModelKind.Trm);

        Assert.False(verification.IsValid);
        Assert.Contains("TRM Component", Assert.Single(verification.Errors));
    }

    [Fact]
    public async Task VerifyAsyncReturnsCountsAndFirstImportWarningWhenDatabaseIsEmptyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "TRM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "TD001", "Technology", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "TRM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "TP001", "Observability", "TD001 Technology", "Capability description", "Capability comments"]
                ]),
            new WorkbookSheet(
                "TRM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "TC001", "Monitoring", "TP001 Observability", "Component description", "Component comments", "Graylog"]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath);

        Assert.True(verification.IsValid);
        Assert.Equal(1, verification.DomainRowCount);
        Assert.Equal(1, verification.CapabilityRowCount);
        Assert.Equal(1, verification.ComponentRowCount);
        Assert.Equal(1, verification.DomainsToAdd);
        Assert.Equal(1, verification.CapabilitiesToAdd);
        Assert.Equal(1, verification.ComponentsToAdd);
        Assert.Contains("first TRM model", Assert.Single(verification.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsyncReturnsCountsForArmWorkbookWhenModelSelectedAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "ARM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "AD001", "Business Apps", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "ARM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "AP008", "Governance, Risk, & Compliance", "AD001 Business Apps", "Capability description", "Capability comments"]
                ]),
            new WorkbookSheet(
                "ARM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "AC001", "Policy Management", "AP008 Governance, Risk, & Compliance", "Component description", "Component comments", "Contoso Workflow"]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath, ReferenceModelKind.Arm);

        Assert.True(verification.IsValid);
        Assert.Equal(ReferenceModelKind.Arm, verification.ModelKind);
        Assert.Equal(1, verification.DomainRowCount);
        Assert.Equal(1, verification.CapabilityRowCount);
        Assert.Equal(1, verification.ComponentRowCount);
        Assert.Equal(1, verification.DomainsToAdd);
        Assert.Equal(1, verification.CapabilitiesToAdd);
        Assert.Equal(1, verification.ComponentsToAdd);
        Assert.Contains("first ARM model", Assert.Single(verification.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsyncReturnsErrorWhenWorkbookDoesNotMatchSelectedModelAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "TRM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "TD001", "Technology", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "TRM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "TP001", "Observability", "TD001 Technology", "Capability description", "Capability comments"]
                ]),
            new WorkbookSheet(
                "TRM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "TC001", "Monitoring", "TP001 Observability", "Component description", "Component comments", "Graylog"]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath, ReferenceModelKind.Arm);

        Assert.False(verification.IsValid);
        Assert.Equal(ReferenceModelKind.Arm, verification.ModelKind);
        Assert.Contains("ARM Domain", Assert.Single(verification.Errors));
    }

    [Fact]
    public async Task ImportAsyncUpdatesExistingRecordsAddsNewOnesAndRecordsVersionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var domain = new TrmDomain
        {
            Code = "TD001",
            Name = "Legacy Domain",
            SourceTitle = "Legacy workbook"
        };
        var legacyCapability = new TrmCapability
        {
            Code = "TP001",
            Name = "Legacy Capability",
            ParentDomain = domain,
            ParentDomainCode = domain.Code
        };
        var existingComponent = new TrmComponent
        {
            Code = "TC001",
            Name = "Legacy Component",
            SourceTitle = "Legacy workbook",
            ParentCapability = legacyCapability,
            ParentCapabilityCode = legacyCapability.Code,
            TechnologyComponentCode = "TECH-001",
            IsCustom = true,
            Description = "Old description",
            Comments = "Old comments",
            ProductExamples = "Legacy example"
        };

        fixture.DbContext.TrmDomains.Add(domain);
        fixture.DbContext.TrmCapabilities.Add(legacyCapability);
        fixture.DbContext.TrmComponents.Add(existingComponent);
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.TrmComponentCapabilityLinks.Add(new TrmComponentCapabilityLink
        {
            TrmComponentId = existingComponent.Id,
            TrmCapabilityId = legacyCapability.Id
        });
        fixture.DbContext.TrmComponentVersions.Add(new TrmComponentVersion
        {
            TrmComponentId = existingComponent.Id,
            VersionNumber = 1,
            ChangeType = "Seed",
            ModelCode = existingComponent.Code,
            Name = existingComponent.Name
        });
        await fixture.DbContext.SaveChangesAsync();

        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "TRM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "TD001", "Technology", "Updated domain description", "Updated domain comments"]
                ]),
            new WorkbookSheet(
                "TRM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "TP001", "Observability", "TD001 Technology", "Updated capability description", "Updated capability comments"],
                    ["Workbook", "TP002", "Security Operations", "TD001 Technology", "New capability", "New capability comments"],
                    ["Workbook", "TP003", "Response", "TD001 Technology", "Another capability", "Another capability comments"]
                ]),
            new WorkbookSheet(
                "TRM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "TC001", "Monitoring Platform", "TP002 Security Operations; TP003 Response", "Updated component description", "Updated component comments", "Sentinel"],
                    ["Workbook", "TC002", "Incident Console", "TP002 Security Operations", "New component description", "New component comments", "Defender"]
                ]));

        var summary = await fixture.CreateService().ImportAsync(workbookPath);

        var persistedDomain = await fixture.DbContext.TrmDomains.SingleAsync(x => x.Code == "TD001");
        var persistedCapabilities = await fixture.DbContext.TrmCapabilities
            .OrderBy(x => x.Code)
            .ToListAsync();
        var persistedComponent = await fixture.DbContext.TrmComponents
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .SingleAsync(x => x.Code == "TC001");
        var newComponent = await fixture.DbContext.TrmComponents.SingleAsync(x => x.Code == "TC002");
        var versions = await fixture.DbContext.TrmComponentVersions
            .AsNoTracking()
            .OrderBy(x => x.TrmComponentId)
            .ThenBy(x => x.VersionNumber)
            .ToListAsync();
        var auditEntry = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal(0, summary.DomainsAdded);
        Assert.Equal(1, summary.DomainsUpdated);
        Assert.Equal(2, summary.CapabilitiesAdded);
        Assert.Equal(1, summary.CapabilitiesUpdated);
        Assert.Equal(1, summary.ComponentsAdded);
        Assert.Equal(1, summary.ComponentsUpdated);

        Assert.Equal("Technology", persistedDomain.Name);
        Assert.Equal(["TP001", "TP002", "TP003"], persistedCapabilities.Select(x => x.Code).ToArray());

        Assert.Equal("Monitoring Platform", persistedComponent.Name);
        Assert.Equal("TP002", persistedComponent.ParentCapabilityCode);
        Assert.False(persistedComponent.IsCustom);
        Assert.Null(persistedComponent.TechnologyComponentCode);
        Assert.Equal(["TP002", "TP003"], persistedComponent.CapabilityLinks.Select(x => x.TrmCapability!.Code).OrderBy(x => x).ToArray());

        Assert.Equal("Incident Console", newComponent.Name);

        Assert.Equal(3, versions.Count);
        Assert.Collection(
            versions,
            version => Assert.Equal("Seed", version.ChangeType),
            version =>
            {
                Assert.Equal("Updated", version.ChangeType);
                Assert.Equal(2, version.VersionNumber);
                Assert.Equal("TC001", version.ModelCode);
                Assert.Equal("TP002, TP003", version.CapabilityCodes);
                Assert.Equal("TRM workbook import", version.Details);
                Assert.False(version.IsCustom);
                Assert.Null(version.TechnologyComponentCode);
            },
            version =>
            {
                Assert.Equal("Imported", version.ChangeType);
                Assert.Equal(1, version.VersionNumber);
                Assert.Equal("TC002", version.ModelCode);
                Assert.Equal("TRM workbook import", version.Details);
            });

        Assert.Equal("Reference", auditEntry.Category);
        Assert.Equal("Import", auditEntry.Action);
        Assert.Contains("1 components added", auditEntry.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Updated 1 domains, 1 capabilities, 1 components", auditEntry.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsyncImportsBrmWorkbookIntoGroupedHierarchyAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "BRM",
                [
                    ["Title", "Capability Type", "Capability Level", "Value Chain", "Value Chain Segment", "Capability Code", "Capability Name", "Parent Capability", "Capability Description", "Capability Notes", "Capability Assessment", "Display Sequence"],
                    ["Business Capability", "Primary", "1", "Operations", "Fulfilment", "BC001", "Order Handling", "", "Level 1 description", "Level 1 notes", "High", "10"],
                    ["Business Capability", "Primary", "2", "Operations", "Fulfilment", "BC002", "Order Capture", "BC001 Order Handling", "Level 2 description", "Level 2 notes", "Medium", "20"]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath, ReferenceModelKind.Brm);
        Assert.True(verification.IsValid);
        Assert.Equal(ReferenceModelKind.Brm, verification.ModelKind);
        Assert.Equal(1, verification.DomainRowCount);
        Assert.Equal(1, verification.CapabilityRowCount);
        Assert.Equal(1, verification.ComponentRowCount);

        var summary = await fixture.CreateService().ImportAsync(workbookPath, ReferenceModelKind.Brm);

        var persistedDomain = await fixture.DbContext.BrmDomains.SingleAsync();
        var persistedCapability = await fixture.DbContext.BrmCapabilities.SingleAsync();
        var persistedComponent = await fixture.DbContext.BrmComponents
            .Include(x => x.ParentCapability)
            .SingleAsync();

        Assert.Equal(ReferenceModelKind.Brm, summary.ModelKind);
        Assert.Equal(1, summary.DomainsAdded);
        Assert.Equal(1, summary.CapabilitiesAdded);
        Assert.Equal(1, summary.ComponentsAdded);
        Assert.StartsWith("BD", persistedDomain.Code, StringComparison.Ordinal);
        Assert.Equal("BC001", persistedCapability.Code);
        Assert.Equal("BC002", persistedComponent.Code);
        Assert.Equal(persistedDomain.Id, persistedCapability.ParentDomainId);
        Assert.Equal(persistedCapability.Id, persistedComponent.ParentCapabilityId);
        Assert.Equal("BC001", persistedComponent.ParentCapabilityCode);
    }

    [Fact]
    public async Task ImportAsyncImportsArmWorkbookIntoDedicatedTablesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "ARM Domain",
                [
                    ["Source", "Code", "Name", "Description", "Comments"],
                    ["Workbook", "AD001", "Business Apps", "Domain description", "Domain comments"]
                ]),
            new WorkbookSheet(
                "ARM Capability",
                [
                    ["Source", "Code", "Name", "Parent Domain", "Description", "Comments"],
                    ["Workbook", "AP008", "Governance, Risk, & Compliance", "AD001 Business Apps", "Capability description", "Capability comments"],
                    ["Workbook", "AP011", "Automation", "AD001 Business Apps", "Capability two description", "Capability two comments"]
                ]),
            new WorkbookSheet(
                "ARM Component",
                [
                    ["Source", "Code", "Name", "Parent Capability", "Description", "Comments", "Product examples"],
                    ["Workbook", "AC012", "Policy Management", "AP008 Governance, Risk, & Compliance; AP011 Automation", "Component description", "Component comments", "Contoso Workflow"]
                ]));

        var summary = await fixture.CreateService().ImportAsync(workbookPath, ReferenceModelKind.Arm);

        var persistedDomain = await fixture.DbContext.ArmDomains.SingleAsync();
        var persistedCapabilities = await fixture.DbContext.ArmCapabilities
            .OrderBy(x => x.Code)
            .ToListAsync();
        var persistedComponent = await fixture.DbContext.ArmComponents
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.ArmCapability)
            .SingleAsync();

        Assert.Equal(ReferenceModelKind.Arm, summary.ModelKind);
        Assert.Equal(1, summary.DomainsAdded);
        Assert.Equal(2, summary.CapabilitiesAdded);
        Assert.Equal(1, summary.ComponentsAdded);
        Assert.Equal("AD001", persistedDomain.Code);
        Assert.Equal(["AP008", "AP011"], persistedCapabilities.Select(x => x.Code).ToArray());
        Assert.Equal("AC012", persistedComponent.Code);
        Assert.Equal("AP008", persistedComponent.ParentCapabilityCode);
        Assert.Equal(["AP008", "AP011"], persistedComponent.CapabilityLinks.Select(x => x.ArmCapability!.Code).OrderBy(x => x).ToArray());
        Assert.Empty(await fixture.DbContext.TrmDomains.ToListAsync());
        Assert.Empty(await fixture.DbContext.TrmCapabilities.ToListAsync());
        Assert.Empty(await fixture.DbContext.TrmComponents.ToListAsync());
    }

    [Fact]
    public async Task ImportAsyncImportsDrmWorkbookIntoDedicatedTablesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "Topic Type",
                [
                    ["Topic Type", "Topic Type ID", "Topic Type Description"],
                    ["Core", "DY001", "Core data"]
                ]),
            new WorkbookSheet(
                "Topic",
                [
                    ["Title", "Topic Name", "Topic Type", "Topic ID", "Alternative Names", "Topic Description", "Comments"],
                    ["Curriculum", "Curriculum", "Core", "DT007", "Courses", "Topic description", "Topic comments"]
                ]),
            new WorkbookSheet(
                "Entity",
                [
                    ["Title", "Data Entity Name", "Parent Topic", "Parent Topic Type", "Data Entity ID", "Alternative Names", "Data Entity Description", "Comments", "TOGAF Enterprise Metamodel Entity"],
                    ["Programme", "Programme of Learning", "DT007 Curriculum", "Core", "DE161", "Programme", "Entity description", "Entity comments", "Course"]
                ]),
            new WorkbookSheet(
                "Common Sub-Class",
                [
                    ["Title", "Sub-Class Name", "Sub-Class ID", "Parent Data Entity", "Alternative Names", "Sub-Class Description", "Sub-Class Comments"],
                    ["Award", "Award Programme", "DE901", "DE161 Programme of Learning", "Award", "Sub-class description", "Sub-class comments"]
                ]));

        var summary = await fixture.CreateService().ImportAsync(workbookPath, ReferenceModelKind.Drm);

        var topicType = await fixture.DbContext.DrmTopicTypes.SingleAsync();
        var topic = await fixture.DbContext.DrmTopics.Include(x => x.TopicType).SingleAsync();
        var entity = await fixture.DbContext.DrmEntities.Include(x => x.ParentTopic).SingleAsync();
        var subClass = await fixture.DbContext.DrmCommonSubClasses.Include(x => x.ParentEntity).SingleAsync();

        Assert.Equal(ReferenceModelKind.Drm, summary.ModelKind);
        Assert.Equal(1, summary.DomainsAdded);
        Assert.Equal(1, summary.CapabilitiesAdded);
        Assert.Equal(2, summary.ComponentsAdded);
        Assert.Equal("DY001", topicType.Code);
        Assert.Equal(topicType.Id, topic.TopicTypeId);
        Assert.Equal("DT007", topic.Code);
        Assert.Equal(topic.Id, entity.ParentTopicId);
        Assert.Equal("DE161", entity.Code);
        Assert.Equal(entity.Id, subClass.ParentEntityId);
        Assert.Equal("DE901", subClass.Code);
    }

    [Fact]
    public async Task VerifyAsyncRejectsDrmDuplicateDataEntityCodesAcrossSheetsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var workbookPath = fixture.WriteWorkbook(
            new WorkbookSheet(
                "Topic Type",
                [
                    ["Topic Type", "Topic Type ID", "Topic Type Description"],
                    ["Core", "DY001", "Core data"]
                ]),
            new WorkbookSheet(
                "Topic",
                [
                    ["Title", "Topic Name", "Topic Type", "Topic ID", "Alternative Names", "Topic Description", "Comments"],
                    ["Curriculum", "Curriculum", "Core", "DT007", "", "", ""]
                ]),
            new WorkbookSheet(
                "Entity",
                [
                    ["Title", "Data Entity Name", "Parent Topic", "Parent Topic Type", "Data Entity ID", "Alternative Names", "Data Entity Description", "Comments", "TOGAF Enterprise Metamodel Entity"],
                    ["Programme", "Programme of Learning", "DT007 Curriculum", "Core", "DE161", "", "", "", ""]
                ]),
            new WorkbookSheet(
                "Common Sub-Class",
                [
                    ["Title", "Sub-Class Name", "Sub-Class ID", "Parent Data Entity", "Alternative Names", "Sub-Class Description", "Sub-Class Comments"],
                    ["Award", "Award Programme", "DE161", "DE161 Programme of Learning", "", "", ""]
                ]));

        var verification = await fixture.CreateService().VerifyAsync(workbookPath, ReferenceModelKind.Drm);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Errors, error => error.Contains("duplicate data entity code 'DE161'", StringComparison.OrdinalIgnoreCase));
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

        public TrmWorkbookImportService CreateService() =>
            new(DbContext, new ComponentVersioningService(DbContext), new AuditLogService(DbContext));

        public string WriteWorkbook(params WorkbookSheet[] sheets)
        {
            var path = Path.Combine(tempDirectory.Path, $"{Guid.NewGuid():N}.xlsx");

            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteDocument(
                archive,
                "xl/workbook.xml",
                BuildWorkbookDocument(sheets));
            WriteDocument(
                archive,
                "xl/_rels/workbook.xml.rels",
                BuildWorkbookRelationshipsDocument(sheets.Length));

            for (var index = 0; index < sheets.Length; index++)
            {
                WriteDocument(
                    archive,
                    $"xl/worksheets/sheet{index + 1}.xml",
                    BuildWorksheetDocument(sheets[index].Rows));
            }

            return path;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
            tempDirectory.Dispose();
        }

        private static void WriteDocument(ZipArchive archive, string entryName, XDocument document)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            document.Save(stream);
        }

        private static XDocument BuildWorkbookDocument(IReadOnlyList<WorkbookSheet> sheets)
        {
            XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            return new XDocument(
                new XElement(
                    spreadsheetNs + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", relationshipNs.NamespaceName),
                    new XElement(
                        spreadsheetNs + "sheets",
                        sheets.Select((sheet, index) =>
                            new XElement(
                                spreadsheetNs + "sheet",
                                new XAttribute("name", sheet.Name),
                                new XAttribute("sheetId", index + 1),
                                new XAttribute(relationshipNs + "id", $"rId{index + 1}"))))));
        }

        private static XDocument BuildWorkbookRelationshipsDocument(int sheetCount)
        {
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            return new XDocument(
                new XElement(
                    packageRelationshipNs + "Relationships",
                    Enumerable.Range(1, sheetCount).Select(index =>
                        new XElement(
                            packageRelationshipNs + "Relationship",
                            new XAttribute("Id", $"rId{index}"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                            new XAttribute("Target", $"worksheets/sheet{index}.xml")))));
        }

        private static XDocument BuildWorksheetDocument(IReadOnlyList<IReadOnlyList<string>> rows)
        {
            XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            return new XDocument(
                new XElement(
                    spreadsheetNs + "worksheet",
                    new XElement(
                        spreadsheetNs + "sheetData",
                        rows.Select((row, rowIndex) =>
                            new XElement(
                                spreadsheetNs + "row",
                                new XAttribute("r", rowIndex + 1),
                                row.Select((value, columnIndex) =>
                                    new XElement(
                                        spreadsheetNs + "c",
                                        new XAttribute("r", $"{GetColumnReference(columnIndex + 1)}{rowIndex + 1}"),
                                        new XAttribute("t", "inlineStr"),
                                        new XElement(
                                            spreadsheetNs + "is",
                                            new XElement(spreadsheetNs + "t", value)))))))));
        }

        private static string GetColumnReference(int index)
        {
            var value = index;
            var columnName = string.Empty;

            while (value > 0)
            {
                value--;
                columnName = (char)('A' + (value % 26)) + columnName;
                value /= 26;
            }

            return columnName;
        }
    }

    private sealed record WorkbookSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"herm-mapper-workbook-tests-{Guid.NewGuid():N}");
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
