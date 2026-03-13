using HERMMapperApp.Services;
using Xunit;

namespace HERMMapperApp.Tests.Services;

public sealed class PasswordSecurityServiceTests
{
    [Fact]
    public void PasswordPolicyRejectsWeakPassword()
    {
        var result = PasswordPolicyService.Validate("short");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("12 characters", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("uppercase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("special", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PasswordHashRoundTripsPassword()
    {
        var hash = PasswordHashService.HashPassword("ChangeMeNow!123");

        Assert.True(PasswordHashService.VerifyPassword("ChangeMeNow!123", hash));
        Assert.False(PasswordHashService.VerifyPassword("WrongPassword!123", hash));
    }
}