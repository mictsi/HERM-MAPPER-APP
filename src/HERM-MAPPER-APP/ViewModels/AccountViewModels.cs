using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HERMMapperApp.ViewModels;

public sealed class LoginViewModel
{
    [Required]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    [BindNever]
    public bool LocalLoginEnabled { get; init; } = true;

    [BindNever]
    public bool OpenIdConnectEnabled { get; init; }

    [BindNever]
    public string OpenIdConnectDisplayName { get; init; } = "OpenID Connect";
}

public sealed class PasswordSelfServiceViewModel
{
    public int? Id { get; init; }

    public string GivenName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string RoleName { get; init; } = AppRoles.User;

    public string? StatusMessage { get; init; }

    public string? ErrorMessage { get; init; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
