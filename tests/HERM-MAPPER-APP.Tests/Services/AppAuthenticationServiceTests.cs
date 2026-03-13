using System.Security.Claims;
using HERM_MAPPER_APP.Configuration;
using HERM_MAPPER_APP.Services;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class AppAuthenticationServiceTests
{
    [Fact]
    public void CreateProperties_UsesConfiguredSessionTimeout()
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
    public void CreateExternalPrincipal_MapsRoleClaims_FromConfiguredGroups()
    {
        var securityOptions = new AuthenticationSecurityOptions(
            SessionTimeoutMinutes: 60,
            MaxFailedLoginAttempts: 15,
            LockoutMinutes: 1);
        var oidcOptions = new OpenIdConnectAuthenticationOptions
        {
            Enabled = true,
            Authority = "https://login.example.com",
            ClientId = "client-id",
            RoleGroupMappings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [HERM_MAPPER_APP.Models.AppRoles.Administrator] = ["group-admin"],
                [HERM_MAPPER_APP.Models.AppRoles.Viewer] = ["group-viewer"]
            }
        };
        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-123"),
            new Claim("name", "Ada Lovelace"),
            new Claim("email", "ada@example.com"),
            new Claim("groups", "[\"group-admin\",\"group-viewer\"]")
        ], "oidc"));
        var service = new AppAuthenticationService(securityOptions);

        var principal = service.CreateExternalPrincipal(externalPrincipal, oidcOptions, "oidc");

        Assert.Equal("Ada Lovelace", principal.Identity?.Name);
        Assert.True(principal.IsInRole(HERM_MAPPER_APP.Models.AppRoles.Administrator));
        Assert.True(principal.IsInRole(HERM_MAPPER_APP.Models.AppRoles.Viewer));
        Assert.True(AppAuthenticationService.IsOpenIdConnectUser(principal));
    }
}
