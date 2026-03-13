using System.Security.Claims;
using HERMMapperApp.Configuration;
using HERMMapperApp.Services;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class AppAuthenticationServiceTests
{
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
}
