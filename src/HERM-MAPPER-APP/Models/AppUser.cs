using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace HERM_MAPPER_APP.Models;

public sealed class AppUser
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string GivenName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required, StringLength(400)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string RoleName { get; set; } = AppRoles.Viewer;

    public int FailedLoginCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime PasswordChangedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => $"{GivenName} {LastName}".Trim();
}

public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Viewer = "Viewer";
    public const string Contributor = "Contributor";

    public const string Admin = Administrator;
    public const string User = Viewer;

    private const string LegacyAdmin = "Admin";
    private const string LegacyUser = "User";

    public static IReadOnlyList<string> All => [Administrator, Viewer, Contributor];

    public static string Normalize(string? roleName)
    {
        var trimmed = roleName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (string.Equals(trimmed, Administrator, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, LegacyAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return Administrator;
        }

        if (string.Equals(trimmed, Viewer, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, LegacyUser, StringComparison.OrdinalIgnoreCase))
        {
            return Viewer;
        }

        if (string.Equals(trimmed, Contributor, StringComparison.OrdinalIgnoreCase))
        {
            return Contributor;
        }

        return trimmed;
    }

    public static bool IsSupported(string? roleName) =>
        All.Contains(Normalize(roleName), StringComparer.Ordinal);

    public static bool IsAdministrator(ClaimsPrincipal principal) =>
        principal.IsInRole(Administrator) || principal.IsInRole(LegacyAdmin);

    public static bool CanReadCatalogue(ClaimsPrincipal principal) =>
        IsAdministrator(principal) || principal.IsInRole(Viewer) || principal.IsInRole(Contributor) || principal.IsInRole(LegacyUser);

    public static bool CanManageProductsAndServices(ClaimsPrincipal principal) =>
        IsAdministrator(principal) || principal.IsInRole(Contributor);
}

public static class AppPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string CatalogueRead = "CatalogueRead";
    public const string ProductsAndServicesWrite = "ProductsAndServicesWrite";
}