using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_SeedsDefaultLifecycleStatuses()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var lifecycleStatuses = await new ConfigurableFieldService(fixture.DbContext)
            .GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus);

        Assert.Equal(
            ConfigurableFieldNames.GetDefaultValues(ConfigurableFieldNames.LifecycleStatus),
            lifecycleStatuses.Select(x => x.Value).ToList());
    }

    [Fact]
    public async Task InitializeAsync_DoesNotDuplicateDefaultLifecycleStatuses()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.Add(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.LifecycleStatus,
            Value = "Production"
        });
        await fixture.DbContext.SaveChangesAsync();

        var initializer = fixture.CreateInitializer();

        await initializer.InitializeAsync();

        var productionCount = await fixture.DbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .CountAsync(x => x.FieldName == ConfigurableFieldNames.LifecycleStatus && x.Value == "Production");

        Assert.Equal(1, productionCount);
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
