using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class ConfigurableFieldServiceTests
{
    [Fact]
    public async Task GetMultiSelectListAsync_IncludesUnknownSelectedValues_OnlyOnce()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
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
    public async Task GetSelectListAsync_AddsUnknownSelection_AfterConfiguredOptions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.ConfigurableFieldOptions.AddRange(
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

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            DbContext = dbContext;
            Service = new ConfigurableFieldService(dbContext);
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

            return new TestFixture(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
