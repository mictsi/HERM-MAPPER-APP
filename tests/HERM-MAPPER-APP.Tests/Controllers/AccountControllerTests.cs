using HERMMapperApp.Controllers;
using HERMMapperApp.Configuration;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HERMMapperApp.Tests.Controllers;

public sealed class AccountControllerTests
{
    [Fact]
    public async Task LoginInvalidPasswordLocksUserAfterConfiguredFailures()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            UserName = "ada",
            PasswordHash = PasswordHashService.HashPassword("ComplexPass!123"),
            RoleName = AppRoles.Viewer
        });
        await fixture.DbContext.SaveChangesAsync();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var controller = fixture.CreateController();
            var result = await controller.LoginAsync(new LoginViewModel
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
    public async Task LoginLockedUserShowsLockoutMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Grace",
            LastName = "Hopper",
            Email = "grace@example.com",
            UserName = "grace",
            PasswordHash = PasswordHashService.HashPassword("ComplexPass!123"),
            RoleName = AppRoles.Viewer,
            LockoutEndUtc = DateTime.UtcNow.AddMinutes(1)
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = "grace",
            Password = "ComplexPass!123"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("temporarily locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoginWhenLocalLoginIsDisabledShowsConfigurationMessage()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(localAuthenticationEnabled: false);
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = "ada",
            Password = "ComplexPass!123"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("Local login is disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExternalLoginWhenOpenIdConnectIsDisabledReturnsNotFound()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(openIdConnectEnabled: false);
        var result = controller.ExternalLogin();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ExternalLoginWhenOpenIdConnectIsEnabledChallengesConfiguredScheme()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(openIdConnectEnabled: true);
        var result = controller.ExternalLogin("/Products");

        var challengeResult = Assert.IsType<ChallengeResult>(result);
        Assert.Single(challengeResult.AuthenticationSchemes);
        Assert.Equal(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme, challengeResult.AuthenticationSchemes[0]);
        Assert.Equal("/Products", challengeResult.Properties?.RedirectUri);
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

        public AccountController CreateController(bool localAuthenticationEnabled = true, bool openIdConnectEnabled = false)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuthenticationService, StubAuthenticationService>();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };

            var controller = new AccountController(
                DbContext,
                new AppAuthenticationService(new AuthenticationSecurityOptions(60, 3, 1)),
                new AuditLogService(DbContext),
                new AuthenticationSecurityOptions(60, 3, 1),
                new LocalAuthenticationOptions
                {
                    Enabled = localAuthenticationEnabled
                },
                new OpenIdConnectAuthenticationOptions
                {
                    Enabled = openIdConnectEnabled,
                    Authority = openIdConnectEnabled ? "https://login.example.com" : string.Empty,
                    ClientId = openIdConnectEnabled ? "client-id" : string.Empty
                })
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
