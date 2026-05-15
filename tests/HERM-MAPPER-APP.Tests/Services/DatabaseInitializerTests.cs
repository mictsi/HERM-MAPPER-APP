using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsyncSeedsDefaultLifecycleStatusesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var lifecycleStatuses = await new ConfigurableFieldService(fixture.DbContext)
            .GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus);

        Assert.Equal(
            ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus),
            lifecycleStatuses.Select(x => x.Value).ToList());

        var displayTimeZone = await fixture.DbContext.AppSettings
            .AsNoTracking()
            .SingleAsync(x => x.Key == AppSettingKeys.DisplayTimeZone);

        Assert.Equal(AppSettingDefaults.DisplayTimeZone, displayTimeZone.Value);
    }

    [Fact]
    public async Task InitializeAsyncDoesNotDuplicateDefaultLifecycleStatusesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.Add(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.LifecycleStatus,
            Value = "Production",
            SortOrder = 1
        });
        await fixture.DbContext.SaveChangesAsync();

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var productionCount = await fixture.DbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .CountAsync(x => x.FieldName == ConfigurableFieldNames.LifecycleStatus && x.Value == "Production");

        Assert.Equal(1, productionCount);
    }

    [Fact]
    public async Task InitializeAsyncBackfillsAndNormalizesSortOrderForExistingOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team B",
                SortOrder = 0,
                CreatedUtc = DateTime.UtcNow.AddMinutes(1)
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Team A",
                SortOrder = 0,
                CreatedUtc = DateTime.UtcNow
            });
        await fixture.DbContext.SaveChangesAsync();

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var ownerOptions = await new ConfigurableFieldService(fixture.DbContext)
            .GetOptionsAsync(ConfigurableFieldNames.Owner);

        Assert.Equal([1, 2], ownerOptions.Select(x => x.SortOrder).ToArray());
        Assert.Equal(["Team A", "Team B"], ownerOptions.Select(x => x.Value).ToArray());
    }

    [Fact]
    public async Task InitializeAsyncNormalizesLegacyUserRolesAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.AppUsers.AddRangeAsync(
            new AppUser
            {
                GivenName = "Legacy",
                LastName = "Admin",
                Email = "legacy-admin@example.com",
                UserName = "legacy-admin",
                PasswordHash = "hash",
                RoleName = "Admin"
            },
            new AppUser
            {
                GivenName = "Legacy",
                LastName = "Viewer",
                Email = "legacy-user@example.com",
                UserName = "legacy-user",
                PasswordHash = "hash",
                RoleName = "User"
            });
        await fixture.DbContext.SaveChangesAsync();

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var roles = await fixture.DbContext.AppUsers
            .AsNoTracking()
            .OrderBy(x => x.UserName)
            .Select(x => x.RoleName)
            .ToListAsync();

        Assert.Equal([AppRoles.Administrator, AppRoles.Viewer], roles);
    }

    [Fact]
    public async Task InitializeAsyncBackfillsLegacyCapabilitiesIntoPrimaryBrmModelAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.BusinessCapabilityCatalogItems.AddAsync(new BusinessCapabilityCatalogItem
        {
            Name = "Legacy capability"
        });
        await fixture.DbContext.SaveChangesAsync();

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var brmModel = await fixture.DbContext.BrmModels.SingleAsync();
        var capability = await fixture.DbContext.BusinessCapabilityCatalogItems.SingleAsync();

        Assert.Equal("Primary BRM Model", brmModel.Name);
        Assert.Equal(brmModel.Id, capability.BrmModelId);
    }

    [Fact]
    public async Task InitializeAsyncAddsMissingDrmTopicForeignKeyColumnAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DrmModelDataEntities""");
        await fixture.DbContext.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DrmCommonSubClasses""");
        await fixture.DbContext.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DrmEntities""");
        await fixture.DbContext.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DrmTopics""");
        await fixture.DbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "DrmTopics" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DrmTopics" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL
            )
            """);

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var columns = await GetSqliteColumnsAsync(fixture.DbContext, "DrmTopics");
        Assert.Contains("TopicTypeId", columns);

        fixture.DbContext.DrmTopicTypes.Add(new DrmTopicType { Code = "DY001", Name = "Topic type" });
        await fixture.DbContext.SaveChangesAsync();

        var topicType = await fixture.DbContext.DrmTopicTypes.SingleAsync();
        fixture.DbContext.DrmTopics.Add(new DrmTopic
        {
            Code = "DT001",
            Name = "Topic",
            TopicTypeId = topicType.Id
        });
        await fixture.DbContext.SaveChangesAsync();

        var topic = await fixture.DbContext.DrmTopics
            .Include(x => x.TopicType)
            .SingleAsync();

        Assert.Equal("DY001", topic.TopicType?.Code);
    }

    private static async Task<HashSet<string>> GetSqliteColumnsAsync(AppDbContext dbContext, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}')";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
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

            return new TestFixture(connection, dbContext);
        }

        public DatabaseInitializer CreateInitializer()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            return new DatabaseInitializer(
                DbContext,
                new TrmWorkbookImportService(DbContext, new ComponentVersioningService(DbContext), new AuditLogService(DbContext)),
                new SampleRelationshipImportService(DbContext),
                configuration,
                NullLogger<DatabaseInitializer>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
