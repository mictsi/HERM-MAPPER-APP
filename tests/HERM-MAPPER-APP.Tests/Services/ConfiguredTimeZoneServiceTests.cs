using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class ConfiguredTimeZoneServiceTests
{
    [Fact]
    public async Task FormatUtcAsync_TreatsUnspecifiedValuesAsUtc_AndUsesConfiguredZone()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DisplayTimeZone,
            Value = "W. Europe Standard Time",
            UpdatedUtc = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var service = new ConfiguredTimeZoneService(new AppSettingsService(fixture.DbContext));
        var formatted = await service.FormatUtcAsync(new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal("2026-03-13 13:00", formatted);
    }

    [Fact]
    public async Task GetTimeZoneAsync_FallsBackToUtc_WhenConfiguredZoneIsInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DisplayTimeZone,
            Value = "Invalid/Zone",
            UpdatedUtc = DateTime.UtcNow
        });
        await fixture.DbContext.SaveChangesAsync();

        var service = new ConfiguredTimeZoneService(new AppSettingsService(fixture.DbContext));
        var timeZone = await service.GetTimeZoneAsync();

        Assert.Equal(TimeZoneInfo.Utc.Id, timeZone.Id);
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

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}