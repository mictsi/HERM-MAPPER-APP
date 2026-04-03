using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class ConfigurableFieldServiceTests
{
    [Fact]
    public async Task GetMultiSelectListAsyncIncludesUnknownSelectedValuesOnlyOnceAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Platform Team",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.Owner,
                Value = "Finance Team",
                SortOrder = 2
            });
        await fixture.DbContext.SaveChangesAsync();

        var items = await fixture.Service.GetMultiSelectListAsync(
            ConfigurableFieldNames.Owner,
            [" Finance Team ", "Unknown Team", "unknown team", ""]);

        Assert.Equal(["Platform Team", "Finance Team", "Unknown Team"], items.Select(x => x.Value).ToArray());
        Assert.False(items[0].Selected);
        Assert.True(items[1].Selected);
        Assert.True(items[2].Selected);
    }

    [Fact]
    public async Task GetSelectListAsyncAddsUnknownSelectionAfterConfiguredOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddRangeAsync(
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = "Production",
                SortOrder = 1
            },
            new ConfigurableFieldOption
            {
                FieldName = ConfigurableFieldNames.LifecycleStatus,
                Value = "Trial",
                SortOrder = 2
            });
        await fixture.DbContext.SaveChangesAsync();

        var items = await fixture.Service.GetSelectListAsync(
            ConfigurableFieldNames.LifecycleStatus,
            "Pilot",
            defaultLabel: "Select status");

        Assert.Equal(["", "Production", "Trial", "Pilot"], items.Select(x => x.Value).ToArray());
        Assert.True(items[3].Selected);
        Assert.False(items[0].Selected);
    }

    [Fact]
    public async Task InvalidateOptionsCausesNextReadToReloadFromDatabaseAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddAsync(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "Platform Team",
            SortOrder = 1
        });
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(["Platform Team"], (await fixture.Service.GetOptionsAsync(ConfigurableFieldNames.Owner)).Select(x => x.Value).ToArray());

        await fixture.DbContext.ConfigurableFieldOptions.AddAsync(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.Owner,
            Value = "Finance Team",
            SortOrder = 2
        });
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(["Platform Team"], (await fixture.Service.GetOptionsAsync(ConfigurableFieldNames.Owner)).Select(x => x.Value).ToArray());

        fixture.Service.InvalidateOptions(ConfigurableFieldNames.Owner);

        Assert.Equal(["Platform Team", "Finance Team"], (await fixture.Service.GetOptionsAsync(ConfigurableFieldNames.Owner)).Select(x => x.Value).ToArray());
    }

    [Fact]
    public async Task RefreshCachedOptionsAsyncReloadsUpdatedFieldOptionsAsync()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.DbContext.ConfigurableFieldOptions.AddAsync(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.LifecycleStatus,
            Value = "Production",
            SortOrder = 1
        });
        await fixture.DbContext.SaveChangesAsync();

        Assert.Equal(["Production"], (await fixture.Service.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus)).Select(x => x.Value).ToArray());

        await fixture.DbContext.ConfigurableFieldOptions.AddAsync(new ConfigurableFieldOption
        {
            FieldName = ConfigurableFieldNames.LifecycleStatus,
            Value = "Trial",
            SortOrder = 2
        });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Service.RefreshCachedOptionsAsync(ConfigurableFieldNames.LifecycleStatus);

        Assert.Equal(["Production", "Trial"], (await fixture.Service.GetOptionsAsync(ConfigurableFieldNames.LifecycleStatus)).Select(x => x.Value).ToArray());
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly MemoryCache memoryCache;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext, MemoryCache memoryCache)
        {
            this.connection = connection;
            this.memoryCache = memoryCache;
            DbContext = dbContext;
            Service = new ConfigurableFieldService(dbContext, new ApplicationLookupCache(memoryCache));
        }

        public AppDbContext DbContext { get; }

        public ConfigurableFieldService Service { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var memoryCache = new MemoryCache(new MemoryCacheOptions());

            return new TestFixture(connection, dbContext, memoryCache);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
            memoryCache.Dispose();
        }
    }
}
