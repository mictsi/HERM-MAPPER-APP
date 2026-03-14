using HERMMapperApp.Controllers;
using HERMMapperApp.Configuration;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
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
    public async Task LoginAsyncSuccessfulAuthenticationRedirectsHomeWhenReturnUrlIsExternal()
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

        using var controller = fixture.CreateController();
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = "ada",
            Password = "ComplexPass!123",
            ReturnUrl = "https://contoso.example/reports"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
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

    [Fact]
    public async Task LoginGetRedirectsAuthenticatedUsersHome()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = controller.Login();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task LoginGetCopiesErrorIntoModelState()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController();
        var result = controller.Login(returnUrl: "/Reports", error: "Sign-in failed");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LoginViewModel>(view.Model);

        Assert.Equal("/Reports", model.ReturnUrl);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage == "Sign-in failed");
    }

    [Fact]
    public async Task LoginAsyncSuccessfulAuthenticationRedirectsToLocalReturnUrlAndWritesAudit()
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

        using var controller = fixture.CreateController();
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = " ada ",
            Password = "ComplexPass!123",
            ReturnUrl = "/Reports"
        });

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Reports", redirect.Url);

        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();
        Assert.Equal("Login", audit.Action);
        Assert.Contains("ada", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAsyncClearsExpiredLockoutBeforeSuccessfulSignIn()
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
            FailedLoginCount = 2,
            LockoutEndUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = "grace",
            Password = "ComplexPass!123"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var user = await fixture.DbContext.AppUsers.SingleAsync();
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEndUtc);
    }

    [Fact]
    public async Task LoginAsyncInvalidPasswordBeforeThresholdIncrementsFailedCount()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.DbContext.AppUsers.Add(new AppUser
        {
            GivenName = "Alan",
            LastName = "Turing",
            Email = "alan@example.com",
            UserName = "alan",
            PasswordHash = PasswordHashService.HashPassword("ComplexPass!123"),
            RoleName = AppRoles.Viewer
        });
        await fixture.DbContext.SaveChangesAsync();

        using var controller = fixture.CreateController();
        var result = await controller.LoginAsync(new LoginViewModel
        {
            UserName = "alan",
            Password = "wrong-password"
        });

        Assert.IsType<ViewResult>(result);
        var user = await fixture.DbContext.AppUsers.SingleAsync();
        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockoutEndUtc);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage == "Invalid login.");
    }

    [Fact]
    public async Task LogoutAsyncExternalUserChallengesOpenIdConnectAndWritesAudit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(
            openIdConnectEnabled: true,
            user: CreatePrincipal("external.user@example.com", AppRoles.Viewer, isLocalUser: false, authenticationType: "oidc"));
        var result = await controller.LogoutAsync();

        var signOut = Assert.IsType<SignOutResult>(result);
        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme, Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme], signOut.AuthenticationSchemes.ToArray());
        Assert.Equal("/Login", signOut.Properties?.RedirectUri);

        var audit = await fixture.DbContext.AuditLogEntries.SingleAsync();
        Assert.Equal("Logout", audit.Action);
        Assert.Equal("ExternalUser", audit.EntityType);
    }

    [Fact]
    public async Task LogoutAsyncLocalUserSignsOutAndRedirectsLogin()
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

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.LogoutAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Login), redirect.ActionName);
        Assert.Equal("Logout", (await fixture.DbContext.AuditLogEntries.SingleAsync()).Action);
    }

    [Fact]
    public async Task ProfileGetRedirectsExternalUsersHome()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(user: CreatePrincipal("external.user@example.com", AppRoles.Viewer, isLocalUser: false, authenticationType: "oidc"));
        var result = await controller.ProfileAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
        Assert.Equal("Password changes are managed by your identity provider.", controller.TempData["ProfileErrorMessage"]);
    }

    [Fact]
    public async Task ProfileGetReturnsPasswordResetViewForLocalUser()
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

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.ProfileAsync();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PasswordReset", view.ViewName);
        var model = Assert.IsType<PasswordSelfServiceViewModel>(view.Model);
        Assert.Equal("ada", model.UserName);
    }

    [Fact]
    public async Task ProfileGetRedirectsLoginWhenCurrentUserMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();

        using var controller = fixture.CreateController(user: CreatePrincipal("missing-user", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.ProfileAsync();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Login), redirect.ActionName);
    }

    [Fact]
    public async Task ProfilePostRejectsIncorrectCurrentPassword()
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

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.ProfileAsync(new PasswordSelfServiceViewModel
        {
            CurrentPassword = "WrongPass!123",
            NewPassword = "UpdatedPass!123",
            ConfirmPassword = "UpdatedPass!123"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PasswordReset", view.ViewName);
        Assert.Contains(controller.ModelState[nameof(PasswordSelfServiceViewModel.CurrentPassword)]!.Errors, error => error.ErrorMessage.Contains("incorrect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProfilePostValidatesNewPasswordPolicy()
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

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.ProfileAsync(new PasswordSelfServiceViewModel
        {
            CurrentPassword = "ComplexPass!123",
            NewPassword = "short",
            ConfirmPassword = "short"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PasswordReset", view.ViewName);
        Assert.NotEmpty(controller.ModelState[nameof(PasswordSelfServiceViewModel.NewPassword)]!.Errors);
    }

    [Fact]
    public async Task ProfilePostUpdatesPasswordAndRedirectsOnSuccess()
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

        using var controller = fixture.CreateController(user: CreatePrincipal("ada", AppRoles.Viewer, isLocalUser: true));
        var result = await controller.ProfileAsync(new PasswordSelfServiceViewModel
        {
            CurrentPassword = "ComplexPass!123",
            NewPassword = "UpdatedPass!123",
            ConfirmPassword = "UpdatedPass!123"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Profile", redirect.ActionName);
        Assert.Equal("Password updated.", controller.TempData["ProfileStatusMessage"]);
        Assert.True(PasswordHashService.VerifyPassword("UpdatedPass!123", (await fixture.DbContext.AppUsers.SingleAsync()).PasswordHash));
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

        public AccountController CreateController(bool localAuthenticationEnabled = true, bool openIdConnectEnabled = false, ClaimsPrincipal? user = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuthenticationService, StubAuthenticationService>();

            var httpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };
            if (user is not null)
            {
                httpContext.User = user;
            }

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

            controller.Url = new TestUrlHelper();

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

    private sealed class TestUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        public string? Action(UrlActionContext actionContext) => actionContext.Action switch
        {
            not null when string.IsNullOrWhiteSpace(actionContext.Controller) => $"/{actionContext.Action}",
            not null => $"/{actionContext.Controller}/{actionContext.Action}",
            _ => "/"
        };

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => !string.IsNullOrWhiteSpace(url) &&
            url[0] == '/' &&
            (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

        public string? Link(string? routeName, object? values) => null;

        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private static ClaimsPrincipal CreatePrincipal(string userName, string roleName, bool isLocalUser, string authenticationType = "test")
    {
        var identity = new ClaimsIdentity(authenticationType);
        identity.AddClaim(new Claim(ClaimTypes.Name, userName));
        identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        if (isLocalUser)
        {
            identity.AddClaim(new Claim(AppAuthenticationService.AuthenticationSourceClaimType, AppAuthenticationService.AuthenticationSourceLocal));
        }
        else
        {
            identity.AddClaim(new Claim(AppAuthenticationService.AuthenticationSourceClaimType, AppAuthenticationService.AuthenticationSourceOpenIdConnect));
        }

        return new ClaimsPrincipal(identity);
    }
}
