using HERM_MAPPER_APP.Controllers;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Controllers;

public sealed class AccountControllerTests
{
    [Fact]
    public async Task Login_InvalidPassword_LocksUserAfterConfiguredFailures()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            UserName = "ada",
            PasswordHash = fixture.PasswordHashService.HashPassword("ComplexPass!123"),
            RoleName = AppRoles.Viewer
        });
        await fixture.DbContext.SaveChangesAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var controller = fixture.CreateController();
            var result = await controller.Login(new LoginViewModel
            {
                UserName = "ada",
                Password = "wrong-password"
            });

            Assert.IsType<ViewResult>(result);
        }

        var user = await fixture.DbContext.AppUsers.SingleAsync();

        Assert.Equal(0, user.FailedLoginCount);
        Assert.NotNull(user.LockoutEndUtc);
        Assert.True(user.LockoutEndUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_LockedUser_ShowsLockoutMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Grace",
            LastName = "Hopper",
            Email = "grace@example.com",
            UserName = "grace",
            PasswordHash = fixture.PasswordHashService.HashPassword("ComplexPass!123"),
            RoleName = AppRoles.Viewer,
            LockoutEndUtc = DateTime.UtcNow.AddMinutes(1)
        });
        await fixture.DbContext.SaveChangesAsync();

        var controller = fixture.CreateController();
        var result = await controller.Login(new LoginViewModel
        {
            UserName = "grace",
            Password = "ComplexPass!123"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("temporarily locked", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestFixture(SqliteConnection connection, AppDbContext dbContext, PasswordHashService passwordHashService)
        {
            this.connection = connection;
            DbContext = dbContext;
            PasswordHashService = passwordHashService;
        }

        public AppDbContext DbContext { get; }

        public PasswordHashService PasswordHashService { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new TestFixture(connection, dbContext, new PasswordHashService());
        }

        public AccountController CreateController()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuthenticationService, StubAuthenticationService>();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };

            var controller = new AccountController(
                DbContext,
                PasswordHashService,
                new PasswordPolicyService(),
                new AppAuthenticationService(new AuthenticationSecurityOptions(60, 3, 1)),
                new AuditLogService(DbContext),
                new AuthenticationSecurityOptions(60, 3, 1))
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };

            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, System.Security.Claims.ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}