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
}