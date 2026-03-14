using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task GetValueAsyncReturnsFallbackWhenSettingDoesNotExist()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var value = await fixture.Service.GetValueAsync("Missing", "Fallback");

        Assert.Equal("Fallback", value);
    }

    [Fact]
    public async Task GetValueAsyncReturnsFallbackWhenStoredValueIsWhitespace()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppSettings.Add(new AppSetting
        {
            Key = "Display",
            Value = "   ",
            UpdatedUtc = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var value = await fixture.Service.GetValueAsync("Display", "Fallback");

        Assert.Equal("Fallback", value);
    }

    [Fact]
    public async Task SetValueAsyncCreatesNewSettingWhenKeyIsMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        await fixture.Service.SetValueAsync("Display", "UTC");

        var setting = await fixture.DbContext.AppSettings.SingleAsync();
        Assert.Equal("Display", setting.Key);
        Assert.Equal("UTC", setting.Value);
        Assert.True(setting.UpdatedUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task SetValueAsyncUpdatesExistingSetting()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppSettings.Add(new AppSetting
        {
            Key = "Display",
            Value = "UTC",
            UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Service.SetValueAsync("Display", "W. Europe Standard Time");

        var settings = await fixture.DbContext.AppSettings.ToListAsync();
        var setting = Assert.Single(settings);
        Assert.Equal("W. Europe Standard Time", setting.Value);
        Assert.True(setting.UpdatedUtc > new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext)
        {
            this.connection = connection;
            DbContext = dbContext;
            Service = new AppSettingsService(dbContext);
        }

        public AppDbContext DbContext { get; }

        public AppSettingsService Service { get; }

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