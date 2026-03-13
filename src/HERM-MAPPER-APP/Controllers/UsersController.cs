using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

[Authorize(Policy = AppPolicies.AdminOnly)]
public sealed class UsersController(
    AppDbContext dbContext,
    PasswordHashService passwordHashService,
    PasswordPolicyService passwordPolicyService,
    AuditLogService auditLogService) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(new UsersIndexViewModel
        {
            StatusMessage = TempData["UsersStatusMessage"] as string,
            ErrorMessage = TempData["UsersErrorMessage"] as string,
            Users = await dbContext.AppUsers
                .AsNoTracking()
                .OrderBy(x => x.GivenName)
                .ThenBy(x => x.LastName)
                .ToListAsync()
        });
    }

    public IActionResult Create() => View(new UserEditViewModel
    {
        RoleOptions = BuildRoleOptions(AppRoles.Viewer)
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserEditViewModel input)
    {
        Normalize(input);

        if (!ModelState.IsValid)
        {
            input.RoleOptions = BuildRoleOptions(input.RoleName);
            return View(input);
        }

        await ValidateUserUniquenessAsync(input.UserName, input.Email, null);
        ValidateRole(input.RoleName);

        var passwordValidation = passwordPolicyService.Validate(input.Password);
        foreach (var error in passwordValidation.Errors)
        {
            ModelState.AddModelError(nameof(UserEditViewModel.Password), error);
        }

        if (!ModelState.IsValid)
        {
            input.RoleOptions = BuildRoleOptions(input.RoleName);
            return View(input);
        }

        var nowUtc = DateTime.UtcNow;
        var user = new AppUser
        {
            GivenName = input.GivenName,
            LastName = input.LastName,
            Email = input.Email,
            UserName = input.UserName,
            RoleName = AppRoles.Normalize(input.RoleName),
            PasswordHash = passwordHashService.HashPassword(input.Password),
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
            PasswordChangedUtc = nowUtc
        };

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync();

        await auditLogService.WriteAsync(
            "Users",
            "Create",
            nameof(AppUser),
            user.Id,
            $"Created user '{user.UserName}'.",
            $"Role: {user.RoleName}.");

        TempData["UsersStatusMessage"] = $"User '{user.UserName}' created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new UserUpdateViewModel
        {
            Id = user.Id,
            GivenName = user.GivenName,
            LastName = user.LastName,
            Email = user.Email,
            UserName = user.UserName,
            RoleName = user.RoleName,
            RoleOptions = BuildRoleOptions(user.RoleName)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserUpdateViewModel input)
    {
        Normalize(input);

        var user = await dbContext.AppUsers.FindAsync(input.Id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            input.RoleOptions = BuildRoleOptions(input.RoleName);
            return View(input);
        }

        await ValidateUserUniquenessAsync(input.UserName, input.Email, input.Id);
        ValidateRole(input.RoleName);

        if (!ModelState.IsValid)
        {
            input.RoleOptions = BuildRoleOptions(input.RoleName);
            return View(input);
        }

        user.GivenName = input.GivenName;
        user.LastName = input.LastName;
        user.Email = input.Email;
        user.UserName = input.UserName;
        user.RoleName = AppRoles.Normalize(input.RoleName);
        user.UpdatedUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        await auditLogService.WriteAsync(
            "Users",
            "Update",
            nameof(AppUser),
            user.Id,
            $"Updated user '{user.UserName}'.",
            $"Role: {user.RoleName}.");

        TempData["UsersStatusMessage"] = $"User '{user.UserName}' updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new UserDeleteViewModel
        {
            Id = user.Id,
            GivenName = user.GivenName,
            LastName = user.LastName,
            Email = user.Email,
            UserName = user.UserName,
            RoleName = user.RoleName
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (string.Equals(User.Identity?.Name, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            TempData["UsersErrorMessage"] = "You cannot delete the account you are signed in with.";
            return RedirectToAction(nameof(Index));
        }

        dbContext.AppUsers.Remove(user);
        await dbContext.SaveChangesAsync();

        await auditLogService.WriteAsync(
            "Users",
            "Delete",
            nameof(AppUser),
            id,
            $"Deleted user '{user.UserName}'.");

        TempData["UsersStatusMessage"] = $"User '{user.UserName}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ResetPassword(int id)
    {
        var user = await dbContext.AppUsers.FindAsync(id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new UserResetPasswordViewModel
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            UserName = user.UserName
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(UserResetPasswordViewModel input)
    {
        var user = await dbContext.AppUsers.FindAsync(input.Id);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            return View(new UserResetPasswordViewModel
            {
                Id = input.Id,
                DisplayName = user.DisplayName,
                UserName = user.UserName,
                Password = input.Password,
                ConfirmPassword = input.ConfirmPassword
            });
        }

        var passwordValidation = passwordPolicyService.Validate(input.Password);
        foreach (var error in passwordValidation.Errors)
        {
            ModelState.AddModelError(nameof(UserResetPasswordViewModel.Password), error);
        }

        if (!ModelState.IsValid)
        {
            return View(new UserResetPasswordViewModel
            {
                Id = input.Id,
                DisplayName = user.DisplayName,
                UserName = user.UserName,
                Password = input.Password,
                ConfirmPassword = input.ConfirmPassword
            });
        }

        user.PasswordHash = passwordHashService.HashPassword(input.Password);
        user.PasswordChangedUtc = DateTime.UtcNow;
        user.UpdatedUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        await auditLogService.WriteAsync(
            "Users",
            "ResetPassword",
            nameof(AppUser),
            user.Id,
            $"Reset password for user '{user.UserName}'.");

        TempData["UsersStatusMessage"] = $"Password reset for '{user.UserName}'.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateUserUniquenessAsync(string userName, string email, int? currentUserId)
    {
        if (await dbContext.AppUsers.AnyAsync(x => x.UserName.ToLower() == userName.ToLower() && x.Id != currentUserId))
        {
            ModelState.AddModelError(nameof(UserEditViewModel.UserName), "Username already exists.");
        }

        if (await dbContext.AppUsers.AnyAsync(x => x.Email.ToLower() == email.ToLower() && x.Id != currentUserId))
        {
            ModelState.AddModelError(nameof(UserEditViewModel.Email), "Email already exists.");
        }
    }

    private void ValidateRole(string roleName)
    {
        if (!AppRoles.IsSupported(roleName))
        {
            ModelState.AddModelError(nameof(UserEditViewModel.RoleName), "Role is not supported.");
        }
    }

    private static IReadOnlyList<SelectListItem> BuildRoleOptions(string selectedRole) =>
        AppRoles.All
            .Select(role => new SelectListItem(role, role, string.Equals(role, selectedRole, StringComparison.Ordinal)))
            .ToList();

    private static void Normalize(UserEditViewModel model)
    {
        model.GivenName = model.GivenName?.Trim() ?? string.Empty;
        model.LastName = model.LastName?.Trim() ?? string.Empty;
        model.Email = model.Email?.Trim() ?? string.Empty;
        model.UserName = model.UserName?.Trim() ?? string.Empty;
        model.RoleName = AppRoles.Normalize(model.RoleName);
    }

    private static void Normalize(UserUpdateViewModel model)
    {
        model.GivenName = model.GivenName?.Trim() ?? string.Empty;
        model.LastName = model.LastName?.Trim() ?? string.Empty;
        model.Email = model.Email?.Trim() ?? string.Empty;
        model.UserName = model.UserName?.Trim() ?? string.Empty;
        model.RoleName = AppRoles.Normalize(model.RoleName);
    }
}