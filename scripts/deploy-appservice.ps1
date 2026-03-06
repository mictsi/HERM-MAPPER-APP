param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$WebAppName,

    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,

    [Parameter(Mandatory = $true)]
    [string]$AppEnvironment,

    [Parameter(Mandatory = $true)]
    [Alias("appplan")]
    [string]$AppPlan
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-CommandExists {
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-AzJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & az @Arguments --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }

    if ([string]::IsNullOrWhiteSpace($output)) {
        return $null
    }

    return $output | ConvertFrom-Json -Depth 100
}

function Invoke-Az {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Get-FlattenedSettings {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [string]$Prefix = ""
    )

    $items = New-Object System.Collections.Generic.List[object]

    if ($null -eq $Value) {
        return $items
    }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $childPrefix = if ([string]::IsNullOrWhiteSpace($Prefix)) { [string]$key } else { "$Prefix`__$key" }
            foreach ($item in Get-FlattenedSettings -Value $Value[$key] -Prefix $childPrefix) {
                $items.Add($item)
            }
        }

        return $items
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($entry in $Value) {
            $childPrefix = if ([string]::IsNullOrWhiteSpace($Prefix)) { [string]$index } else { "$Prefix`__$index" }
            foreach ($item in Get-FlattenedSettings -Value $entry -Prefix $childPrefix) {
                $items.Add($item)
            }

            $index++
        }

        return $items
    }

    $stringValue = switch ($Value) {
        { $_ -is [bool] } { $_.ToString().ToLowerInvariant(); break }
        { $_ -is [datetime] } { $_.ToString("o"); break }
        default { [string]$Value }
    }

    $items.Add([pscustomobject]@{
        Name = $Prefix
        Value = $stringValue
    })

    return $items
}

function Get-AppServiceSettingsArguments {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]]$FlatSettings,

        [Parameter(Mandatory = $true)]
        [string]$AppEnvironmentValue
    )

    $settings = New-Object System.Collections.Generic.List[string]

    foreach ($setting in $FlatSettings) {
        if ([string]::IsNullOrWhiteSpace($setting.Name)) {
            continue
        }

        $settings.Add("$($setting.Name)=$($setting.Value)")
    }

    $settings.Add("ASPNETCORE_ENVIRONMENT=$AppEnvironmentValue")
    $settings.Add("DOTNET_ENVIRONMENT=$AppEnvironmentValue")
    $settings.Add("WEBSITE_RUN_FROM_PACKAGE=1")

    return $settings
}

function Get-ProjectTargetFrameworkMajor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath
    )

    [xml]$projectXml = Get-Content $ProjectPath -Raw
    $framework = $projectXml.Project.PropertyGroup.TargetFramework | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($framework)) {
        throw "TargetFramework was not found in $ProjectPath."
    }

    if ($framework -match '^net(?<major>\d+)\.0$') {
        return [int]$matches['major']
    }

    if ($framework -match '^net(?<major>\d+)$') {
        return [int]$matches['major']
    }

    throw "Unsupported TargetFramework format '$framework' in $ProjectPath."
}

function Get-LinuxDotnetRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [int]$TargetMajor
    )

    $runtimes = Invoke-AzJson -Arguments @("webapp", "list-runtimes", "--os", "linux")
    $dotnetRuntimes = @($runtimes | Where-Object { $_ -like "DOTNETCORE|*" })
    if ($dotnetRuntimes.Count -eq 0) {
        throw "No Linux DOTNETCORE runtimes were returned by Azure CLI."
    }

    $preferred = "DOTNETCORE|$TargetMajor.0"
    if ($dotnetRuntimes -contains $preferred) {
        return $preferred
    }

    $fallback = $dotnetRuntimes |
        Sort-Object {
            if ($_ -match '^DOTNETCORE\|(?<major>\d+)(\.(?<minor>\d+))?') {
                [int]$matches['major']
            }
            else {
                -1
            }
        } -Descending |
        Select-Object -First 1

    Write-Warning "Requested .NET runtime $preferred was not returned by Azure CLI. Falling back to $fallback."
    return $fallback
}

if (-not (Test-CommandExists -Name "az")) {
    throw "Azure CLI 'az' was not found on PATH."
}

if (-not (Test-Path $SettingsFile)) {
    throw "Settings file '$SettingsFile' was not found."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj"
if (-not (Test-Path $projectPath)) {
    throw "Project file '$projectPath' was not found."
}

Write-Step "Selecting Azure subscription"
Invoke-Az -Arguments @("account", "set", "--subscription", $SubscriptionId)

Write-Step "Validating resource group"
$resourceGroupExists = & az group exists --name $ResourceGroupName
if ($LASTEXITCODE -ne 0) {
    throw "Failed to validate resource group '$ResourceGroupName'."
}

if ($resourceGroupExists.Trim().ToLowerInvariant() -ne "true") {
    throw "Resource group '$ResourceGroupName' does not exist. This script requires an existing resource group because no location parameter was provided."
}

$resourceGroup = Invoke-AzJson -Arguments @("group", "show", "--name", $ResourceGroupName)
$location = [string]$resourceGroup.location

Write-Step "Loading application settings from $SettingsFile"
$settingsJson = Get-Content $SettingsFile -Raw | ConvertFrom-Json -Depth 100 -AsHashtable
$flatSettings = Get-FlattenedSettings -Value $settingsJson
$appSettingsArguments = Get-AppServiceSettingsArguments -FlatSettings $flatSettings -AppEnvironmentValue $AppEnvironment

Write-Step "Ensuring App Service plan '$AppPlan' exists"
$plan = $null
try {
    $plan = Invoke-AzJson -Arguments @("appservice", "plan", "show", "--resource-group", $ResourceGroupName, "--name", $AppPlan)
}
catch {
    $plan = $null
}

if ($null -eq $plan) {
    Write-Step "Creating Linux App Service plan '$AppPlan' in $location"
    $plan = Invoke-AzJson -Arguments @(
        "appservice", "plan", "create",
        "--resource-group", $ResourceGroupName,
        "--name", $AppPlan,
        "--location", $location,
        "--sku", "S1",
        "--is-linux"
    )
}

$isLinuxPlan = [bool]$plan.reserved
$targetFrameworkMajor = Get-ProjectTargetFrameworkMajor -ProjectPath $projectPath

Write-Step "Ensuring web app '$WebAppName' exists"
$webApp = $null
try {
    $webApp = Invoke-AzJson -Arguments @("webapp", "show", "--resource-group", $ResourceGroupName, "--name", $WebAppName)
}
catch {
    $webApp = $null
}

if ($null -eq $webApp) {
    if ($isLinuxPlan) {
        $runtime = Get-LinuxDotnetRuntime -TargetMajor $targetFrameworkMajor
        Write-Step "Creating Linux web app '$WebAppName' with runtime $runtime"
        $webApp = Invoke-AzJson -Arguments @(
            "webapp", "create",
            "--resource-group", $ResourceGroupName,
            "--plan", $AppPlan,
            "--name", $WebAppName,
            "--runtime", $runtime
        )
    }
    else {
        Write-Step "Creating Windows web app '$WebAppName'"
        $webApp = Invoke-AzJson -Arguments @(
            "webapp", "create",
            "--resource-group", $ResourceGroupName,
            "--plan", $AppPlan,
            "--name", $WebAppName
        )
    }
}

Write-Step "Publishing application"
$artifactsRoot = Join-Path $repoRoot ".artifacts\appservice\$WebAppName"
$publishDir = Join-Path $artifactsRoot "publish"
$zipPath = Join-Path $artifactsRoot "app.zip"

if (Test-Path $artifactsRoot) {
    Remove-Item $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& dotnet publish $projectPath -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Step "Creating deployment archive"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Step "Applying App Service settings"
$chunkSize = 50
for ($i = 0; $i -lt $appSettingsArguments.Count; $i += $chunkSize) {
    $endIndex = [Math]::Min($i + $chunkSize - 1, $appSettingsArguments.Count - 1)
    $chunk = $appSettingsArguments[$i..$endIndex]
    Invoke-Az -Arguments @(
        "webapp", "config", "appsettings", "set",
        "--resource-group", $ResourceGroupName,
        "--name", $WebAppName,
        "--settings"
    ) + $chunk
}

Write-Step "Deploying ZIP package"
Invoke-Az -Arguments @(
    "webapp", "deploy",
    "--resource-group", $ResourceGroupName,
    "--name", $WebAppName,
    "--src-path", $zipPath,
    "--type", "zip",
    "--restart", "true",
    "--clean", "true"
)

Write-Step "Deployment complete"
$hostName = [string]$webApp.defaultHostName
if ([string]::IsNullOrWhiteSpace($hostName)) {
    $hostName = "$WebAppName.azurewebsites.net"
}

Write-Host "App URL: https://$hostName" -ForegroundColor Green
