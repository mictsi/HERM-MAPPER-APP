using System.Security.Claims;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HERM_MAPPER_APP.Services;

public sealed class AppAuthenticationService(AuthenticationSecurityOptions authenticationSecurityOptions)
{
    public ClaimsPrincipal CreatePrincipal(AppUser user)
    {
        var normalizedRole = AppRoles.Normalize(user.RoleName);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.GivenName),
            new(ClaimTypes.Surname, user.LastName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, normalizedRole)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public AuthenticationProperties CreateProperties(bool isPersistent = false) => new()
    {
        IsPersistent = isPersistent,
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(authenticationSecurityOptions.SessionTimeout)
    };
}