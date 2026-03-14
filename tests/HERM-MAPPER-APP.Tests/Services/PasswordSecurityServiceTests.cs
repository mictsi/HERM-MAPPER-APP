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

    [Fact]
    public void PasswordPolicyAcceptsStrongPasswordAndCalculatesMaximumStrength()
    {
        var result = PasswordPolicyService.Validate("StrongPassword!1234");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(100, result.StrengthScore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("pbkdf2-sha512$invalid$not-base64$still-not-base64")]
    public void PasswordHashRejectsMalformedHashes(string passwordHash)
    {
        Assert.False(PasswordHashService.VerifyPassword("ChangeMeNow!123", passwordHash));
    }
}