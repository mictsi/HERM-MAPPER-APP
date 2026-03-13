using HERM_MAPPER_APP.Services;
using Xunit;

namespace HERM_MAPPER_APP.Tests.Services;

public sealed class PasswordSecurityServiceTests
{
    [Fact]
    public void PasswordPolicy_RejectsWeakPassword()
    {
        var service = new PasswordPolicyService();

        var result = service.Validate("short");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("12 characters", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("uppercase", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("special", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PasswordHash_RoundTripsPassword()
    {
        var service = new PasswordHashService();
        var hash = service.HashPassword("ChangeMeNow!123");

        Assert.True(service.VerifyPassword("ChangeMeNow!123", hash));
        Assert.False(service.VerifyPassword("WrongPassword!123", hash));
    }
}