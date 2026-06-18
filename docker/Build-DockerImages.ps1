[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateNotNullOrEmpty()]
    [string] $ImageName = 'herm-mapper-app',

    [ValidateNotNullOrEmpty()]
    [string] $Tag = 'local',

    [ValidateNotNullOrEmpty()]
    [string[]] $Platform = @('linux/amd64', 'linux/arm64'),

    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'images'),

    [ValidateSet('auto', 'plain', 'tty', 'quiet')]
    [string] $Progress = 'auto',

    [switch] $Load,

    [switch] $NoCache,

    [switch] $Pull
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RepoRoot {
    param([string] $StartPath = $PSScriptRoot)

    $current = (Resolve-Path -LiteralPath $StartPath).Path
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $current 'HERM-MAPPER-APP.sln')) {
            return $current
        }

        $parent = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            throw "Could not locate repository root from '$StartPath'."
        }

        $current = $parent
    }
}

function Invoke-Docker {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [switch] $CaptureOutput
    )

    Write-Host "docker $($Arguments -join ' ')" -ForegroundColor Cyan

    if ($CaptureOutput) {
        $output = & docker @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "docker command failed with exit code $LASTEXITCODE."
        }

        return $output
    }

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker command failed with exit code $LASTEXITCODE."
    }
}

function Assert-DockerCli {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI was not found on PATH. Install Docker Desktop or add docker to PATH before running this script.'
    }
}

function ConvertTo-DockerPlatform {
    param([string] $Value)

    switch ($Value.Trim().ToLowerInvariant()) {
        'all' { return @('linux/amd64', 'linux/arm64') }
        'amd64' { return @('linux/amd64') }
        'x64' { return @('linux/amd64') }
        'x86_64' { return @('linux/amd64') }
        'linux/x64' { return @('linux/amd64') }
        'linux/x86_64' { return @('linux/amd64') }
        'linux/amd64' { return @('linux/amd64') }
        'arm64' { return @('linux/arm64') }
        'aarch64' { return @('linux/arm64') }
        'linux/aarch64' { return @('linux/arm64') }
        'linux/arm64' { return @('linux/arm64') }
        default { throw "Unsupported platform '$Value'. Use linux/amd64, linux/arm64, x64, arm64, or all." }
    }
}

function Resolve-DockerPlatforms {
    param([string[]] $RequestedPlatforms)

    $resolved = New-Object System.Collections.Generic.List[string]
    foreach ($requestedPlatform in $RequestedPlatforms) {
        foreach ($platformValue in $requestedPlatform.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            foreach ($resolvedPlatform in ConvertTo-DockerPlatform -Value $platformValue) {
                if (-not $resolved.Contains($resolvedPlatform)) {
                    $resolved.Add($resolvedPlatform)
                }
            }
        }
    }

    if ($resolved.Count -eq 0) {
        throw 'No Docker platforms were requested.'
    }

    return $resolved.ToArray()
}

function Get-PlatformTagSuffix {
    param([string] $DockerPlatform)

    switch ($DockerPlatform) {
        'linux/amd64' { return 'amd64' }
        'linux/arm64' { return 'arm64' }
        default { throw "Unsupported Docker platform '$DockerPlatform'." }
    }
}

function Get-ArchiveName {
    param(
        [string] $DockerPlatform,
        [string] $Image,
        [string] $ImageTag
    )

    $safeImage = $Image -replace '[\\/:*?"<>|]+', '-'
    $safeTag = $ImageTag -replace '[\\/:*?"<>|]+', '-'
    $safePlatform = $DockerPlatform -replace '/', '-'

    return "$safeImage`_$safeTag`_$safePlatform.tar"
}

function Get-DockerServerPlatform {
    $platform = (Invoke-Docker -Arguments @('version', '--format', '{{.Server.Os}}/{{.Server.Arch}}') -CaptureOutput | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($platform)) {
        throw 'Could not detect the Docker server platform.'
    }

    return (ConvertTo-DockerPlatform -Value $platform | Select-Object -First 1)
}

$repoRoot = Get-RepoRoot
$dockerfilePath = Join-Path $repoRoot 'docker\Dockerfile'
$resolvedOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$platforms = @(Resolve-DockerPlatforms -RequestedPlatforms $Platform)

if (-not (Test-Path -LiteralPath $dockerfilePath)) {
    throw "Dockerfile not found: $dockerfilePath"
}

Assert-DockerCli
Invoke-Docker -Arguments @('buildx', 'version') | Out-Null

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$builtArchives = @{}

Push-Location $repoRoot
try {
    foreach ($dockerPlatform in $platforms) {
        $tagSuffix = Get-PlatformTagSuffix -DockerPlatform $dockerPlatform
        $archivePath = Join-Path $resolvedOutputDirectory (Get-ArchiveName -DockerPlatform $dockerPlatform -Image $ImageName -ImageTag $Tag)
        $imageTag = "${ImageName}:${Tag}"
        $platformImageTag = "${ImageName}:${Tag}-${tagSuffix}"
        $output = "type=docker,dest=$archivePath"

        $buildArguments = @(
            'buildx', 'build',
            '--platform', $dockerPlatform,
            '--file', $dockerfilePath,
            '--tag', $imageTag,
            '--tag', $platformImageTag,
            '--output', $output,
            '--progress', $Progress
        )

        if ($NoCache) {
            $buildArguments += '--no-cache'
        }

        if ($Pull) {
            $buildArguments += '--pull'
        }

        $buildArguments += $repoRoot

        if ($PSCmdlet.ShouldProcess($archivePath, "Build $dockerPlatform Docker image archive")) {
            if (Test-Path -LiteralPath $archivePath) {
                Remove-Item -LiteralPath $archivePath -Force
            }

            Invoke-Docker -Arguments $buildArguments
            Write-Host "Wrote $archivePath" -ForegroundColor Green
        }

        $builtArchives[$dockerPlatform] = $archivePath
    }

    if ($Load) {
        $serverPlatform = Get-DockerServerPlatform
        if (-not $builtArchives.ContainsKey($serverPlatform)) {
            throw "The local Docker server platform is '$serverPlatform', but no matching archive was built."
        }

        $archiveToLoad = $builtArchives[$serverPlatform]
        if ($PSCmdlet.ShouldProcess($archiveToLoad, "Load Docker image archive into the local Docker engine")) {
            Invoke-Docker -Arguments @('load', '--input', $archiveToLoad)
        }
    }
}
finally {
    Pop-Location
}
