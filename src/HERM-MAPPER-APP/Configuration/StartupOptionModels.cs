using Microsoft.Extensions.Logging;

namespace HERMMapperApp.Configuration;

public sealed record StartupDiagnosticsOptions(
    bool ConsoleLoggingEnabled,
    LogLevel ConsoleLogLevel,
    bool SqlLoggingEnabled,
    LogLevel SqlLogLevel,
    bool SqlSensitiveDataLoggingEnabled,
    bool SqlDetailedErrorsEnabled);

public sealed record AuthenticationSecurityOptions(
    int SessionTimeoutMinutes,
    int MaxFailedLoginAttempts,
    int LockoutMinutes)
{
    public TimeSpan SessionTimeout => TimeSpan.FromMinutes(SessionTimeoutMinutes);

    public TimeSpan LockoutDuration => TimeSpan.FromMinutes(LockoutMinutes);
}