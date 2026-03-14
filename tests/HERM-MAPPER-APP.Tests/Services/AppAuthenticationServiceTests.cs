using System.Security.Claims;
using HERMMapperApp.Configuration;
using HERMMapperApp.Models;
using HERMMapperApp.Services;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class AppAuthenticationServiceTests
{
    [Fact]
    public void CreatePrincipalNormalizesClaimsForLocalUser()
    {
        var user = new AppUser
        {
            Id = 42,
            GivenName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            UserName = "adal",
            RoleName = "Admin"
        };

        var principal = AppAuthenticationService.CreatePrincipal(user);

        Assert.Equal("adal", principal.Identity?.Name);
        Assert.True(principal.IsInRole(AppRoles.Administrator));
        Assert.True(AppAuthenticationService.IsLocalUser(principal));
        Assert.False(AppAuthenticationService.IsOpenIdConnectUser(principal));
        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("ada@example.com", principal.FindFirstValue(ClaimTypes.Email));
    }

    [Fact]
    public void CreatePropertiesUsesConfiguredSessionTimeout()
    {
        var options = new AuthenticationSecurityOptions(
            SessionTimeoutMinutes: 60,
            MaxFailedLoginAttempts: 15,
            LockoutMinutes: 1);
        var service = new AppAuthenticationService(options);

        var before = DateTimeOffset.UtcNow;
        var properties = service.CreateProperties();
        var after = DateTimeOffset.UtcNow;
        var minimum = before.AddMinutes(60).AddSeconds(-1);
        var maximum = after.AddMinutes(60).AddSeconds(1);

        Assert.NotNull(properties.ExpiresUtc);
        Assert.InRange(properties.ExpiresUtc!.Value, minimum, maximum);
    }

    [Fact]
    public void CreateExternalPrincipalMapsRoleClaimsFromConfiguredGroups()
    {
        var oidcOptions = new OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            Authority = "https://login.example.com",
            ClientId = "client-id",
            RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [HERMMapperApp.Models.AppRoles.Administrator] = ["group-admin"],
                [HERMMapperApp.Models.AppRoles.Viewer] = ["group-viewer"]
            }
        };
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-123"),
            new Claim("name", "Ada Lovelace"),
            new Claim("email", "ada@example.com"),
            new Claim("groups", "[\"group-admin\",\"group-viewer\"]")
        ], "oidc"));
        var principal = AppAuthenticationService.CreateExternalPrincipal(externalPrincipal, oidcOptions, "oidc");

        Assert.Equal("Ada Lovelace", principal.Identity?.Name);
        Assert.True(principal.IsInRole(HERMMapperApp.Models.AppRoles.Administrator));
        Assert.True(principal.IsInRole(HERMMapperApp.Models.AppRoles.Viewer));
        Assert.True(AppAuthenticationService.IsOpenIdConnectUser(principal));
    }

    [Fact]
    public void CreateExternalPrincipalUsesFallbackClaimsAndDeduplicatesGroups()
    {
        var oidcOptions = new OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            Authority = "https://login.example.com",
            ClientId = "client-id",
            NameClaimType = "preferred_username",
            EmailClaimType = "mail",
            GivenNameClaimType = "given_name",
            SurnameClaimType = "family_name",
            RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [AppRoles.Contributor] = ["group-contributor", "group-shared"]
            }
        };
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-456"),
            new Claim("mail", "grace@example.com"),
            new Claim("given_name", "Grace"),
            new Claim("family_name", "Hopper"),
            new Claim("groups", "group-contributor"),
            new Claim("groups", "[\"group-shared\",\"group-contributor\",\"\"]")
        ], "oidc"));

        var principal = AppAuthenticationService.CreateExternalPrincipal(externalPrincipal, oidcOptions, "oidc");

        Assert.Equal("grace@example.com", principal.Identity?.Name);
        Assert.True(principal.IsInRole(AppRoles.Contributor));
        Assert.Equal(
            ["group-contributor", "group-shared"],
            principal.Claims.Where(claim => claim.Type == oidcOptions.EffectiveGroupClaimType).Select(claim => claim.Value).OrderBy(value => value).ToArray());
        Assert.Equal("Grace", principal.FindFirstValue(ClaimTypes.GivenName));
        Assert.Equal("Hopper", principal.FindFirstValue(ClaimTypes.Surname));
    }

    [Fact]
    public void CreateExternalPrincipalThrowsWhenIdentityIsMissing()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppAuthenticationService.CreateExternalPrincipal(null, CreateOidcOptions(), "oidc"));

        Assert.Contains("authenticated identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateExternalPrincipalThrowsWhenNoMappedRolesExist()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-789"),
            new Claim("name", "Alan Turing"),
            new Claim("groups", "group-none")
        ], "oidc"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppAuthenticationService.CreateExternalPrincipal(principal, CreateOidcOptions(), "oidc"));

        Assert.Contains("does not map to any configured application role", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateExternalPrincipalThrowsWhenSubjectIsMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("name", "Alan Turing"),
            new Claim("groups", "group-viewer")
        ], "oidc"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppAuthenticationService.CreateExternalPrincipal(principal, CreateOidcOptions(), "oidc"));

        Assert.Contains("subject identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OpenIdConnectAuthenticationOptions CreateOidcOptions() => new()
    {
        Enabled = true,
        Authority = "https://login.example.com",
        ClientId = "client-id",
        RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [AppRoles.Viewer] = ["group-viewer"]
        }
    };
}
