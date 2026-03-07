param(
    [string]$Project = (Join-Path $PSScriptRoot "src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj"),
    [string]$LaunchProfile = "http",
    [string]$ConsoleLogLevel = "Information",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
# Set requested console level (param) and also ensure default Logging section promotes Information-level messages
# Apply console log level parameter (or default) and ensure default Logging section promotes Information-level messages
if ($ConsoleLogLevel) {
    $env:HERM_Diagnostics__Console__LogLevel = $ConsoleLogLevel
} else {
    $env:HERM_Diagnostics__Console__LogLevel = "Information"
}
# Ensure the `Logging:LogLevel:Default` is at least Information unless explicitly set in env
if (-not $env:HERM_Logging__LogLevel__Default) {
    $env:HERM_Logging__LogLevel__Default = "Information"
}

New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

$arguments = @(
    "run"
    "--project"
    $Project
    "--launch-profile"
    $LaunchProfile
)

if ($NoBuild) {
    $arguments += "--no-build"
}

Write-Host "Starting HERM Mapper..."
Write-Host "Project: $Project"
Write-Host "Launch profile: $LaunchProfile"
Write-Host "Console log level: $ConsoleLogLevel"

& dotnet @arguments
exit $LASTEXITCODE
