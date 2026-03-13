using HERMMapperApp.Controllers;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class UsersControllerTests
{
    [Fact]
    public async Task CreatePersistsUserWithRoleAndHashedPassword()
    {
        await using var fixture = await TestFixture.CreateAsync();
        using var controller = fixture.CreateController();

        var result = await controller.CreateAsync(new UserEditViewModel
        {
            GivenName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            UserName = "adal",
            RoleName = AppRoles.User,
            Password = "ComplexPass!123",
            ConfirmPassword = "ComplexPass!123"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var user = await fixture.DbContext.AppUsers.SingleAsync();
        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();

        Assert.Equal("ada@example.com", user.Email);
        Assert.Equal(AppRoles.User, user.RoleName);
        Assert.NotEqual("ComplexPass!123", user.PasswordHash);
        Assert.True(PasswordHashService.VerifyPassword("ComplexPass!123", user.PasswordHash));
        Assert.Equal("Create", audit.Action);
    }

    [Fact]
    public async Task ResetPasswordUpdatesStoredHash()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Grace",
            LastName = "Hopper",
            Email = "grace@example.com",
            UserName = "grace",
            RoleName = AppRoles.Admin,
            PasswordHash = PasswordHashService.HashPassword("InitialPass!123")
        });
        await fixture.DbContext.SaveChangesAsync();

        var user = await fixture.DbContext.AppUsers.SingleAsync();
        using var controller = fixture.CreateController();

        var result = await controller.ResetPasswordAsync(new UserResetPasswordViewModel
        {
            Id = user.Id,
            Password = "UpdatedPass!123",
            ConfirmPassword = "UpdatedPass!123"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var updatedUser = await fixture.DbContext.AppUsers.SingleAsync();
        Assert.True(PasswordHashService.VerifyPassword("UpdatedPass!123", updatedUser.PasswordHash));
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

        public UsersController CreateController()
        {
            var controller = new UsersController(
                DbContext,
                new AuditLogService(DbContext));

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new TestTempDataProvider());
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}