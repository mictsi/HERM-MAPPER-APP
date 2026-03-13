using System.Globalization;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Data;
using HERMMapperApp.Configuration;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Controllers;

public sealed class AccountController(
    AppDbContext dbContext,
    AppAuthenticationService appAuthenticationService,
    AuditLogService auditLogService,
    AuthenticationSecurityOptions authenticationSecurityOptions,
    LocalAuthenticationOptions localAuthenticationOptions,
    OpenIdConnectAuthenticationOptions openIdConnectAuthenticationOptions) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null, string? error = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        return View(BuildLoginViewModel(returnUrl));
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginAsync(LoginViewModel input)
    {
        input.UserName = input.UserName?.Trim() ?? string.Empty;
        var caseInsensitiveCollation = AppDatabaseCollations.GetCaseInsensitive(dbContext.Database);
        input = BuildLoginViewModel(input.ReturnUrl, input);

        if (!localAuthenticationOptions.Enabled)
        {
            ModelState.AddModelError(string.Empty, "Local login is disabled.");
            return View(input);
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var user = await dbContext.AppUsers
            .SingleOrDefaultAsync(x => EF.Functions.Collate(x.UserName, caseInsensitiveCollation) == input.UserName);

        var nowUtc = DateTime.UtcNow;

        if (user is not null && user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value <= nowUtc)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            user.UpdatedUtc = nowUtc;
            await dbContext.SaveChangesAsync();
        }

        if (user is not null && user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > nowUtc)
        {
            var secondsRemaining = Math.Max(1, (int)Math.Ceiling((user.LockoutEndUtc.Value - nowUtc).TotalSeconds));
            ModelState.AddModelError(string.Empty, $"Account is temporarily locked. Try again in {secondsRemaining} seconds.");
            return View(input);
        }

        if (user is null || !PasswordHashService.VerifyPassword(input.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                user.UpdatedUtc = nowUtc;
                var userWasLockedOut = false;
                string? lockoutEndUtc = null;

                if (user.FailedLoginCount >= authenticationSecurityOptions.MaxFailedLoginAttempts)
                {
                    user.FailedLoginCount = 0;
                    user.LockoutEndUtc = nowUtc.Add(authenticationSecurityOptions.LockoutDuration);
                    userWasLockedOut = true;
                    lockoutEndUtc = user.LockoutEndUtc?.ToString("O", CultureInfo.InvariantCulture);
                }

                await dbContext.SaveChangesAsync();

                if (userWasLockedOut)
                {
                    await auditLogService.WriteAsync(
                        "Authentication",
                        "Lockout",
                        nameof(AppUser),
                        user.Id,
                        $"User '{user.UserName}' was locked out after repeated failed logins.",
                        $"Lockout until {lockoutEndUtc}.");

                    ModelState.AddModelError(string.Empty, $"Account is temporarily locked. Try again in {authenticationSecurityOptions.LockoutMinutes} minute(s).");
                    return View(input);
                }
            }

            ModelState.AddModelError(string.Empty, "Invalid login.");
            return View(input);
        }

        if (user.FailedLoginCount != 0 || user.LockoutEndUtc is not null)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            user.UpdatedUtc = nowUtc;
            await dbContext.SaveChangesAsync();
        }

        await HttpContext.SignInAsync(
            AppAuthenticationService.CreatePrincipal(user),
            appAuthenticationService.CreateProperties());

        await auditLogService.WriteAsync(
            "Authentication",
            "Login",
            nameof(AppUser),
            user.Id,
            $"User '{user.UserName}' signed in.");

        if (!string.IsNullOrWhiteSpace(input.ReturnUrl) && Url.IsLocalUrl(input.ReturnUrl))
        {
            return Redirect(input.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string? returnUrl = null)
    {
        if (!openIdConnectAuthenticationOptions.Enabled)
        {
            return NotFound();
        }

        var redirectUrl = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? Url.Action("Index", "Home")
            : returnUrl;

        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = await GetCurrentUserAsync();
        var externalUserName = User.Identity?.Name;
        var isOpenIdConnectUser = AppAuthenticationService.IsOpenIdConnectUser(User);

        if (user is not null)
        {
            await auditLogService.WriteAsync(
                "Authentication",
                "Logout",
                nameof(AppUser),
                user.Id,
                $"User '{user.UserName}' signed out.");
        }
        else if (isOpenIdConnectUser && !string.IsNullOrWhiteSpace(externalUserName))
        {
            await auditLogService.WriteAsync(
                "Authentication",
                "Logout",
                "ExternalUser",
                null,
                $"OpenID Connect user '{externalUserName}' signed out.");
        }

        if (isOpenIdConnectUser && openIdConnectAuthenticationOptions.Enabled)
        {
            return SignOut(
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action(nameof(Login))
                },
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public async Task<IActionResult> ProfileAsync()
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (!AppAuthenticationService.IsLocalUser(User))
        {
            TempData["ProfileErrorMessage"] = "Password changes are managed by your identity provider.";
            return RedirectToAction("Index", "Home");
        }

        var user = await GetRequiredCurrentUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        return View("PasswordReset", BuildPasswordSelfServiceViewModel(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProfileAsync(PasswordSelfServiceViewModel input)
    {
        if (!AppAuthenticationService.IsLocalUser(User))
        {
            TempData["ProfileErrorMessage"] = "Password changes are managed by your identity provider.";
            return RedirectToAction("Index", "Home");
        }

        var user = await GetRequiredCurrentUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        if (!PasswordHashService.VerifyPassword(input.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(PasswordSelfServiceViewModel.CurrentPassword), "Current password is incorrect.");
            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        var passwordValidation = PasswordPolicyService.Validate(input.NewPassword);
        if (!passwordValidation.IsValid)
        {
            foreach (var error in passwordValidation.Errors)
            {
                ModelState.AddModelError(nameof(PasswordSelfServiceViewModel.NewPassword), error);
            }

            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        user.PasswordHash = PasswordHashService.HashPassword(input.NewPassword);
        user.PasswordChangedUtc = DateTime.UtcNow;
        user.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        await auditLogService.WriteAsync(
            "Users",
            "SelfServicePasswordReset",
            nameof(AppUser),
            user.Id,
            $"User '{user.UserName}' changed their password.");

        TempData["ProfileStatusMessage"] = "Password updated.";
        return RedirectToAction("Profile");
    }

    private async Task<AppUser?> GetCurrentUserAsync()
    {
        if (!AppAuthenticationService.IsLocalUser(User))
        {
            return null;
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await dbContext.AppUsers.SingleOrDefaultAsync(x => x.UserName == userName);
    }

    private async Task<AppUser?> GetRequiredCurrentUserAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user is not null)
        {
            return user;
        }

        await HttpContext.SignOutAsync();
        return null;
    }

    private PasswordSelfServiceViewModel BuildPasswordSelfServiceViewModel(AppUser user, PasswordSelfServiceViewModel? postedModel = null) => new()
    {
        Id = user.Id,
        GivenName = user.GivenName,
        LastName = user.LastName,
        Email = user.Email,
        UserName = user.UserName,
        RoleName = AppRoles.Normalize(user.RoleName),
        StatusMessage = TempData["ProfileStatusMessage"] as string,
        ErrorMessage = TempData["ProfileErrorMessage"] as string,
        CurrentPassword = postedModel?.CurrentPassword ?? string.Empty,
        NewPassword = postedModel?.NewPassword ?? string.Empty,
        ConfirmPassword = postedModel?.ConfirmPassword ?? string.Empty
    };

    private LoginViewModel BuildLoginViewModel(string? returnUrl, LoginViewModel? postedModel = null) => new()
    {
        UserName = postedModel?.UserName ?? string.Empty,
        Password = postedModel?.Password ?? string.Empty,
        ReturnUrl = returnUrl ?? postedModel?.ReturnUrl,
        LocalLoginEnabled = localAuthenticationOptions.Enabled,
        OpenIdConnectEnabled = openIdConnectAuthenticationOptions.Enabled,
        OpenIdConnectDisplayName = openIdConnectAuthenticationOptions.DisplayName
    };
}
