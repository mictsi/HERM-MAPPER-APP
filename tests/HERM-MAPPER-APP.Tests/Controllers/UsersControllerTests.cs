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

    [Fact]
    public async Task IndexFiltersUsersBySearchTerm()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.AddRange(
            BuildUser("Ada", "Lovelace", "ada@example.com", "adal", AppRoles.Admin),
            BuildUser("Grace", "Hopper", "grace@example.com", "ghopper", AppRoles.User),
            BuildUser("Alan", "Turing", "alan@example.com", "aturing", AppRoles.Contributor));
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.IndexAsync("hopper");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<UsersIndexViewModel>(view.Model);

        Assert.Equal("hopper", model.Search);
        Assert.Single(model.Users);
        Assert.Equal("Grace Hopper", model.Users[0].DisplayName);
        Assert.Equal(1, model.TotalCount);
    }

    [Fact]
    public async Task IndexPagesUsers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        for (var index = 1; index <= 12; index++)
        {
            fixture.DbContext.AppUsers.Add(BuildUser(
                $"User{index:00}",
                "Example",
                $"user{index:00}@example.com",
                $"user{index:00}",
                AppRoles.User));
        }

        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.IndexAsync(page: 2);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<UsersIndexViewModel>(view.Model);

        Assert.Equal(2, model.Page);
        Assert.Equal(10, model.PageSize);
        Assert.Equal(12, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(2, model.Users.Count);
        Assert.Equal("User11 Example", model.Users[0].DisplayName);
        Assert.Equal("User12 Example", model.Users[1].DisplayName);
    }

    private static AppUser BuildUser(string givenName, string lastName, string email, string userName, string roleName) => new()
    {
        GivenName = givenName,
        LastName = lastName,
        Email = email,
        UserName = userName,
        RoleName = roleName,
        PasswordHash = PasswordHashService.HashPassword("Password!123"),
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PasswordChangedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

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
