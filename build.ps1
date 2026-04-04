[CmdletBinding()]
param(
    [ValidateSet("Prod")]
    [string]$Target = "Prod",

    [ValidateNotNullOrEmpty()]
    [string]$Runtime = "All",

    [string]$VersionOverride,

    [string]$InformationalVersionOverride,

    [switch]$Clean,

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

function Get-AssemblyVersion {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $null
    }

    $parts = $Version.Split(".", [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -gt 4) {
        throw "VersionOverride '$Version' must have between 1 and 4 numeric parts."
    }

    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "VersionOverride '$Version' must contain numeric parts only."
        }
    }

    $normalizedParts = @($parts)
    while ($normalizedParts.Count -lt 4) {
        $normalizedParts += "0"
    }

    return ($normalizedParts -join ".")
}

function Resolve-Runtimes {
    param([string]$RuntimeArgument)

    if ($RuntimeArgument -eq "All") {
        return @("linux-x64", "linux-arm64", "win-x64", "win-arm64")
    }

    return @(
        $RuntimeArgument.Split(",") |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

$repoRoot = Get-RepoRoot
$projectPath = Join-Path $repoRoot "src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj"
$outputRoot = Join-Path $repoRoot "artifacts\prod"
$dotnetHome = Join-Path $repoRoot ".dotnet-cli"

if (-not (Test-Path $projectPath)) {
    throw "Application project not found: $projectPath"
}

if ($Target -ne "Prod") {
    throw "Unsupported build target '$Target'."
}

$runtimes = @(Resolve-Runtimes -RuntimeArgument $Runtime)
if ($runtimes.Count -eq 0) {
    throw "No runtimes were resolved from '$Runtime'."
}

$assemblyVersion = Get-AssemblyVersion -Version $VersionOverride

New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
$env:DOTNET_CLI_HOME = $dotnetHome

if ($Clean -and (Test-Path $outputRoot)) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

Push-Location $repoRoot
try {
    foreach ($runtimeIdentifier in $runtimes) {
        $publishDirectory = Join-Path $outputRoot $runtimeIdentifier
        if (Test-Path $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }

        Invoke-DotNet -Arguments @("restore", $projectPath, "-r", $runtimeIdentifier)

        $publishArguments = @(
            "publish",
            $projectPath,
            "-c", $Configuration,
            "--no-restore",
            "-r", $runtimeIdentifier,
            "--self-contained", "false",
            "/p:UseAppHost=false",
            "-o", $publishDirectory
        )

        if (-not [string]::IsNullOrWhiteSpace($VersionOverride)) {
            $publishArguments += "/p:Version=$VersionOverride"
            $publishArguments += "/p:FileVersion=$assemblyVersion"
            $publishArguments += "/p:AssemblyVersion=$assemblyVersion"
        }

        if (-not [string]::IsNullOrWhiteSpace($InformationalVersionOverride)) {
            $publishArguments += "/p:InformationalVersion=$InformationalVersionOverride"
        }

        Invoke-DotNet -Arguments $publishArguments
    }
}
finally {
    Pop-Location
}
