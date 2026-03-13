using System.Text.Json;
using HERM_MAPPER_APP.Configuration;
using System.Security.Claims;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HERM_MAPPER_APP.Services;

public sealed class AppAuthenticationService(AuthenticationSecurityOptions authenticationSecurityOptions)
{
    public const string AuthenticationSourceClaimType = "herm:auth_source";
    public const string AuthenticationSourceLocal = "local";
    public const string AuthenticationSourceOpenIdConnect = "oidc";
    public const string IdentityProviderClaimType = "herm:identity_provider";

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
            new(ClaimTypes.Role, normalizedRole),
            new(AuthenticationSourceClaimType, AuthenticationSourceLocal),
            new(IdentityProviderClaimType, CookieAuthenticationDefaults.AuthenticationScheme)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    public ClaimsPrincipal CreateExternalPrincipal(
        ClaimsPrincipal? externalPrincipal,
        OpenIdConnectAuthenticationOptions options,
        string authenticationScheme)
    {
        var sourceIdentity = externalPrincipal?.Identities.FirstOrDefault(identity => identity.IsAuthenticated);
        if (sourceIdentity is null)
        {
            throw new InvalidOperationException("OpenID Connect sign-in did not include an authenticated identity.");
        }

        var sourceClaims = sourceIdentity.Claims.ToList();
        var groupIds = ExtractGroupIds(sourceClaims, options.EffectiveGroupClaimType);
        var roles = ResolveExternalRoles(groupIds, options);

        if (roles.Count == 0)
        {
            throw new InvalidOperationException("Your account does not map to any configured application role.");
        }

        var subject = FindFirstClaimValue(sourceClaims, options.EffectiveSubjectClaimType, ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("OpenID Connect sign-in did not include a subject identifier.");
        }

        var userName = FindFirstClaimValue(
            sourceClaims,
            options.EffectiveNameClaimType,
            "preferred_username",
            "name",
            options.EffectiveEmailClaimType,
            ClaimTypes.Email,
            subject);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, userName),
            new(AuthenticationSourceClaimType, AuthenticationSourceOpenIdConnect),
            new(IdentityProviderClaimType, authenticationScheme)
        };

        AddClaimIfPresent(claims, ClaimTypes.Email, FindFirstClaimValue(sourceClaims, options.EffectiveEmailClaimType, ClaimTypes.Email));
        AddClaimIfPresent(claims, ClaimTypes.GivenName, FindFirstClaimValue(sourceClaims, options.EffectiveGivenNameClaimType, ClaimTypes.GivenName));
        AddClaimIfPresent(claims, ClaimTypes.Surname, FindFirstClaimValue(sourceClaims, options.EffectiveSurnameClaimType, ClaimTypes.Surname));

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var groupId in groupIds)
        {
            claims.Add(new Claim(options.EffectiveGroupClaimType, groupId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    public AuthenticationProperties CreateProperties(bool isPersistent = false) => new()
    {
        IsPersistent = isPersistent,
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(authenticationSecurityOptions.SessionTimeout)
    };

    public static bool IsLocalUser(ClaimsPrincipal principal) =>
        principal.HasClaim(AuthenticationSourceClaimType, AuthenticationSourceLocal);

    public static bool IsOpenIdConnectUser(ClaimsPrincipal principal) =>
        principal.HasClaim(AuthenticationSourceClaimType, AuthenticationSourceOpenIdConnect);

    private static IReadOnlyList<string> ResolveExternalRoles(
        IReadOnlyCollection<string> groupIds,
        OpenIdConnectAuthenticationOptions options)
    {
        var normalizedGroups = new HashSet<string>(groupIds, StringComparer.OrdinalIgnoreCase);
        var matchedRoles = new List<string>();

        foreach (var roleMapping in options.RoleGroupMappings)
        {
            if (roleMapping.Value.Any(normalizedGroups.Contains))
            {
                matchedRoles.Add(roleMapping.Key);
            }
        }

        return matchedRoles
            .Select(AppRoles.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractGroupIds(IEnumerable<Claim> claims, string groupClaimType)
    {
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in claims.Where(claim => string.Equals(claim.Type, groupClaimType, StringComparison.Ordinal)))
        {
            foreach (var groupId in ParseClaimValues(claim.Value))
            {
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    groupIds.Add(groupId.Trim());
                }
            }
        }

        return groupIds.ToArray();
    }

    private static IReadOnlyList<string> ParseClaimValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsedValues = JsonSerializer.Deserialize<string[]>(trimmedValue);
                if (parsedValues is not null)
                {
                    return parsedValues
                        .Where(parsedValue => !string.IsNullOrWhiteSpace(parsedValue))
                        .ToArray();
                }
            }
            catch (JsonException)
            {
            }
        }

        return [trimmedValue];
    }

    private static string FindFirstClaimValue(IEnumerable<Claim> claims, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = claims
                .FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
                ?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static void AddClaimIfPresent(ICollection<Claim> claims, string claimType, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            claims.Add(new Claim(claimType, value));
        }
    }
}
