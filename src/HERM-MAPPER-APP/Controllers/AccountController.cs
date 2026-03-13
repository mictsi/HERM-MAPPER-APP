using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class AccountController(
    AppDbContext dbContext,
    PasswordHashService passwordHashService,
    PasswordPolicyService passwordPolicyService,
    AppAuthenticationService appAuthenticationService,
    AuditLogService auditLogService,
    AuthenticationSecurityOptions authenticationSecurityOptions) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel input)
    {
        input.UserName = input.UserName?.Trim() ?? string.Empty;
        var normalizedUserName = input.UserName.ToUpperInvariant();

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var user = await dbContext.AppUsers
            .SingleOrDefaultAsync(x => x.UserName == input.UserName || x.UserName.ToUpper() == normalizedUserName);

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

        if (user is null || !passwordHashService.VerifyPassword(input.Password, user.PasswordHash))
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
                    lockoutEndUtc = user.LockoutEndUtc?.ToString("O");
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
            appAuthenticationService.CreatePrincipal(user),
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = await GetCurrentUserAsync();

        await HttpContext.SignOutAsync();

        if (user is not null)
        {
            await auditLogService.WriteAsync(
                "Authentication",
                "Logout",
                nameof(AppUser),
                user.Id,
                $"User '{user.UserName}' signed out.");
        }

        return RedirectToAction(nameof(Login));
    }

    public async Task<IActionResult> Profile()
    {
        var user = await GetRequiredCurrentUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        return View("PasswordReset", BuildPasswordSelfServiceViewModel(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(PasswordSelfServiceViewModel input)
    {
        var user = await GetRequiredCurrentUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        if (!passwordHashService.VerifyPassword(input.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(PasswordSelfServiceViewModel.CurrentPassword), "Current password is incorrect.");
            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        var passwordValidation = passwordPolicyService.Validate(input.NewPassword);
        if (!passwordValidation.IsValid)
        {
            foreach (var error in passwordValidation.Errors)
            {
                ModelState.AddModelError(nameof(PasswordSelfServiceViewModel.NewPassword), error);
            }

            return View("PasswordReset", BuildPasswordSelfServiceViewModel(user, input));
        }

        user.PasswordHash = passwordHashService.HashPassword(input.NewPassword);
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
        return RedirectToAction(nameof(Profile));
    }

    private async Task<AppUser?> GetCurrentUserAsync()
    {
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
}