param(
    [string]$Project = (Join-Path $PSScriptRoot "HERM-MAPPER-APP.csproj"),
    [string]$Urls = "http://127.0.0.1:5056",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"

New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

$arguments = @(
    "run"
    "--project"
    $Project
    "--urls"
    $Urls
)

if ($NoBuild) {
    $arguments += "--no-build"
}

Write-Host "Starting HERM Mapper..."
Write-Host "Project: $Project"
Write-Host "URLs: $Urls"

& dotnet @arguments
exit $LASTEXITCODE
