[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepoRoot {
    param([string]$StartPath = $PSScriptRoot)

    $current = (Resolve-Path -LiteralPath $StartPath).Path
    while ($true) {
        if (Test-Path (Join-Path $current "HERM-MAPPER-APP.sln")) {
            return $current
        }

        $parent = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate repository root from '$StartPath'."
        }

        $current = $parent
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Get-RepoRoot
$solutionPath = Join-Path $repoRoot "HERM-MAPPER-APP.sln"
$testProjectPath = Join-Path $repoRoot "tests\HERM-MAPPER-APP.Tests\HERM-MAPPER-APP.Tests.csproj"
$dotnetHome = Join-Path $repoRoot ".dotnet-cli"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

if (-not (Test-Path $testProjectPath)) {
    throw "Test project not found: $testProjectPath"
}

New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
$env:DOTNET_CLI_HOME = $dotnetHome

Push-Location $repoRoot
try {
    Invoke-DotNet -Arguments @("restore", $solutionPath)
    Invoke-DotNet -Arguments @("build", $solutionPath, "-c", $Configuration, "--no-restore", "/p:UseAppHost=false")
    Invoke-DotNet -Arguments @("test", $testProjectPath, "-c", $Configuration, "--no-build", "--logger", "trx;LogFileName=test-results.trx")
}
finally {
    Pop-Location
}
