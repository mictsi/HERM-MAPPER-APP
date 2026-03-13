using System.ComponentModel.DataAnnotations;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class UsersIndexViewModel
{
    public string? StatusMessage { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<AppUser> Users { get; init; } = [];
}

public sealed class UserEditViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "Given name")]
    public string GivenName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role")]
    public string RoleName { get; set; } = AppRoles.User;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IReadOnlyList<SelectListItem> RoleOptions { get; set; } = [];
}

public sealed class UserUpdateViewModel
{
    [Required]
    public int Id { get; set; }

    [Required, StringLength(100)]
    [Display(Name = "Given name")]
    public string GivenName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role")]
    public string RoleName { get; set; } = AppRoles.User;

    public IReadOnlyList<SelectListItem> RoleOptions { get; set; } = [];
}

public sealed class UserDeleteViewModel
{
    public int Id { get; init; }

    public string GivenName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string RoleName { get; init; } = AppRoles.User;
}

public sealed class UserResetPasswordViewModel
{
    public int Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}