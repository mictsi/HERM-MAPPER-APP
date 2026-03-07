<#
Deploy to Azure App Service using Azure CLI.

Usage:
.\deploy-appservice-azcli.ps1 -SubscriptionId <sub> -Location <region> -ResourceGroupName <rg> -WebAppName <name> -SettingsFile <appsettings.json> -AppEnvironment <Environment> [-AppServicePlanName <plan>] [-NamePrefix <prefix>]

This script:
- Uses an existing App Service plan (errors if not found)
- Creates the Web App if it does not exist (uses the given plan)
- Publishes the project (dotnet), zips output and deploys via `az webapp deploy`
- Reads the provided settings file, flattens hierarchical keys using `__` and applies them as App Settings
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$Location,

    [string]$NamePrefix = "securejournal",
    [string]$AppServicePlanName = "",

    [Parameter(Mandatory = $true)]
    [string]$WebAppName,

    [string]$AppServiceSku = "B1",
    [string]$AppServiceRuntime = "DOTNETCORE:10.0",

    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,

    [Parameter(Mandatory = $true)]
    [string]$AppEnvironment
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }

function New-RandomSuffix {
    param([int]$Length = 6)
    $chars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()
    -join (1..$Length | ForEach-Object { $chars[(Get-Random -Minimum 0 -Maximum $chars.Length)] })
}

function ConvertTo-NormalizedPrefix {
    param([string]$InputPrefix)
    $value = $InputPrefix.ToLowerInvariant()
    $value = $value -replace "[^a-z0-9-]", ""
    if ([string]::IsNullOrWhiteSpace($value)) { return "securejournal" }
    return $value
}

function Test-CommandExists { param([string]$Name) return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue) }

function Invoke-AzJson {
    param([string[]]$Arguments)
    $output = & az @Arguments --output json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI command failed: az $($Arguments -join ' ')`n$output" }
    if ([string]::IsNullOrWhiteSpace($output)) { return $null }
    return $output | ConvertFrom-Json -Depth 100
}

function Invoke-Az { param([string[]]$Arguments) & az @Arguments 2>&1; if ($LASTEXITCODE -ne 0) { throw "Azure CLI command failed: az $($Arguments -join ' ')" } }

function Get-FlattenedSettings {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [string]$Prefix = ''
    )

    $items = New-Object System.Collections.Generic.List[psobject]
    if ($null -eq $Value) { return $items }

    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $childPrefix = if ([string]::IsNullOrWhiteSpace($Prefix)) { [string]$key } else { "$Prefix`__$key" }
            foreach ($item in Get-FlattenedSettings -Value $Value[$key] -Prefix $childPrefix) { $items.Add($item) }
        }
        return $items
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $index = 0
        foreach ($entry in $Value) {
            $childPrefix = if ([string]::IsNullOrWhiteSpace($Prefix)) { [string]$index } else { "$Prefix`__$index" }
            foreach ($item in Get-FlattenedSettings -Value $entry -Prefix $childPrefix) { $items.Add($item) }
            $index++
        }
        return $items
    }

    if ($Value -is [bool]) { $stringValue = $Value.ToString().ToLowerInvariant() }
    elseif ($Value -is [datetime]) { $stringValue = $Value.ToString('o') }
    else { $stringValue = [string]$Value }

    $items.Add([pscustomobject]@{ Name = $Prefix; Value = $stringValue })
    return $items
}

function Get-AppServiceSettingsArguments {
    param([System.Collections.Generic.List[psobject]]$FlatSettings, [string]$AppEnvironmentValue)
    $settings = New-Object System.Collections.Generic.List[string]
    foreach ($setting in $FlatSettings) {
        if ([string]::IsNullOrWhiteSpace($setting.Name)) { continue }
        $settings.Add("$($setting.Name)=$($setting.Value)")
    }
    $settings.Add("ASPNETCORE_ENVIRONMENT=$AppEnvironmentValue")
    $settings.Add("DOTNET_ENVIRONMENT=$AppEnvironmentValue")
    # NOTE: Do NOT set WEBSITE_RUN_FROM_PACKAGE=1 when using writable local files (SQLite). Running from package mounts the app as read-only.
    return $settings
}

function Get-ProjectTargetFrameworkMajor {
    param([string]$ProjectPath)
    if (-not (Test-Path $ProjectPath)) { throw "Project file not found: $ProjectPath" }
    [xml]$projectXml = Get-Content $ProjectPath -Raw
    $framework = $projectXml.Project.PropertyGroup.TargetFramework | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($framework)) { throw "TargetFramework was not found in $ProjectPath." }
    if ($framework -match '^net(?<major>\d+)\.0$') { return [int]$matches['major'] }
    if ($framework -match '^net(?<major>\d+)$') { return [int]$matches['major'] }
    throw "Unsupported TargetFramework format '$framework' in $ProjectPath."
}

function Get-LinuxDotnetRuntime {
    param([int]$TargetMajor)
    $runtimes = Invoke-AzJson -Arguments @('webapp','list-runtimes','--os','linux')
    $dotnetRuntimes = @($runtimes | Where-Object { $_ -like 'DOTNETCORE|*' })
    if ($dotnetRuntimes.Count -eq 0) { throw 'No Linux DOTNETCORE runtimes were returned by Azure CLI.' }
    $preferred = "DOTNETCORE|$TargetMajor.0"
    if ($dotnetRuntimes -contains $preferred) { return $preferred }
    $fallback = $dotnetRuntimes | Sort-Object {
        if ($_ -match '^DOTNETCORE\|(?<major>\d+)(\.(?<minor>\d+))?') { [int]$matches['major'] } else { -1 }
    } -Descending | Select-Object -First 1
    Write-Warning "Requested .NET runtime $preferred was not returned by Azure CLI. Falling back to $fallback."
    return $fallback
    $runtimes = $null
    try {
        $runtimes = Invoke-AzJson -Arguments @('webapp','list-runtimes','--os','linux')
    } catch {
        Write-Warning "Failed to retrieve runtimes from Azure CLI: $_. Falling back to a constructed DOTNETCORE runtime."
        return "DOTNETCORE|$TargetMajor.0"
    }

    if ($null -eq $runtimes -or $runtimes.Count -eq 0) {
        Write-Warning "Azure CLI returned no runtimes. Falling back to DOTNETCORE|$TargetMajor.0"
        return "DOTNETCORE|$TargetMajor.0"
    }

    # Try common dotnet runtime identifiers
    $candidates = @(
        "DOTNETCORE|$TargetMajor.0",
        "DOTNET|$TargetMajor.0",
        "DOTNETCORE|$TargetMajor",
        "DOTNET|$TargetMajor"
    )

    foreach ($c in $candidates) {
        if ($runtimes -contains $c) { return $c }
    }

    # Find any entry containing DOTNET or DOTNETCORE
    $dotnetRuntimes = @($runtimes | Where-Object { $_ -match 'DOTNET' })
    if ($dotnetRuntimes.Count -gt 0) {
        # choose the highest major version available
        $chosen = $dotnetRuntimes | Sort-Object {
            if ($_ -match 'DOTNET(?:CORE)?\|(?<major>\d+)') { [int]$matches['major'] } else { -1 }
        } -Descending | Select-Object -First 1
        Write-Warning "Using available runtime '$chosen' since exact match for $TargetMajor was not found."
        return $chosen
    }

    # Last resort: construct a reasonable runtime string
    Write-Warning "No DOTNET runtime identifiers found in Azure CLI runtime list. Falling back to DOTNETCORE|$TargetMajor.0"
    return "DOTNETCORE|$TargetMajor.0"
}

### Begin main flow
Write-Step 'Validating prerequisites'
if (-not (Test-CommandExists 'az')) { throw "Azure CLI 'az' was not found on PATH." }
if (-not (Test-CommandExists 'dotnet')) { throw "dotnet SDK was not found on PATH." }

Write-Step 'Selecting Azure subscription'
Invoke-Az -Arguments @('account','set','--subscription',$SubscriptionId)

# Determine repository root so script works when run from repo root or other locations.
function Get-RepoRoot {
    $start = (Get-Location).ProviderPath
    $cur = $start
    while ($true) {
        if ((Test-Path (Join-Path $cur 'HERM-MAPPER-APP.sln')) -or (Test-Path (Join-Path $cur '.git')) -or (Test-Path (Join-Path $cur 'src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj'))) {
            return $cur
        }
        $parent = Split-Path $cur -Parent
        if (([string]::IsNullOrWhiteSpace($parent)) -or ($parent -eq $cur)) { break }
        $cur = $parent
    }
    # fallback to script location parent
    return Split-Path -Parent $PSScriptRoot
}

$repoRoot = Get-RepoRoot
$projectPath = Join-Path $repoRoot 'src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj'

# Resolve settings file path: accept absolute, repo-relative, or cwd-relative paths
if (-not (Test-Path $SettingsFile)) {
    $maybe = Join-Path $repoRoot $SettingsFile
    if (Test-Path $maybe) { $SettingsFile = $maybe } else { throw "Settings file '$SettingsFile' was not found (tried cwd and repo root)." }
} else {
    $SettingsFile = (Resolve-Path -Path $SettingsFile).ProviderPath
}

Write-Step "Creating/updating resource group '$ResourceGroupName' in '$Location'..."
Invoke-Az -Arguments @(
    'group','create',
    '--name',$ResourceGroupName,
    '--location',$Location,
    '--output','none'
) | Out-Null

Write-Step "Loading application settings from $SettingsFile"
$settingsJson = Get-Content $SettingsFile -Raw | ConvertFrom-Json -Depth 100
$flatSettings = Get-FlattenedSettings -Value $settingsJson
$appSettingsArguments = Get-AppServiceSettingsArguments -FlatSettings $flatSettings -AppEnvironmentValue $AppEnvironment

# Ensure App Service plan exists (create if missing)
$prefix = ConvertTo-NormalizedPrefix -InputPrefix $NamePrefix
if ([string]::IsNullOrWhiteSpace($AppServicePlanName)) {
    $suffix = New-RandomSuffix
    $AppServicePlanName = "$prefix-asp-$suffix"
    if ($AppServicePlanName.Length -gt 40) { $AppServicePlanName = $AppServicePlanName.Substring(0,40) }
}

if ($AppServicePlanName -notmatch '^[a-zA-Z0-9-]{1,40}$') { throw 'AppServicePlanName must be 1-40 chars, letters/numbers/hyphens only.' }

Write-Step "Creating/updating App Service plan '$AppServicePlanName' (SKU: $AppServiceSku, Linux)..."
Invoke-Az -Arguments @(
    'appservice','plan','create',
    '--name',$AppServicePlanName,
    '--resource-group',$ResourceGroupName,
    '--location',$Location,
    '--sku',$AppServiceSku,
    '--is-linux',
    '--output','none'
) | Out-Null

# Retrieve plan
$plan = $null
try { $plan = Invoke-AzJson -Arguments @('appservice','plan','show','--resource-group',$ResourceGroupName,'--name',$AppServicePlanName) } catch { $plan = $null }

# Determine whether the App Service plan is Linux-backed.
$isLinuxPlan = $false
if ($null -ne $plan) {
    if ($plan.PSObject.Properties.Name -contains 'reserved') {
        try { $isLinuxPlan = [bool]$plan.reserved } catch { $isLinuxPlan = $false }
    } elseif ($plan.kind) {
        if ($plan.kind -match 'linux') { $isLinuxPlan = $true }
    } else {
        try {
            $reservedVal = & az appservice plan show --resource-group $ResourceGroupName --name $AppServicePlanName --query reserved -o tsv
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($reservedVal)) { $isLinuxPlan = [bool]::Parse($reservedVal) }
        } catch { $isLinuxPlan = $false }
    }
}

Write-Step "Checking if web app '$WebAppName' exists"
$webAppExists = $false
try {
    $existingName = (Invoke-Az -Arguments @('webapp','show','--resource-group',$ResourceGroupName,'--name',$WebAppName,'--query','name','--output','tsv'))
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingName)) { $webAppExists = $true }
} catch { $webAppExists = $false }

if (-not $webAppExists) {
    Write-Step "Creating web app '$WebAppName'..."
    if ($isLinuxPlan) {
        # prefer explicit runtime if provided, otherwise detect from project
        if (-not [string]::IsNullOrWhiteSpace($AppServiceRuntime)) {
            $runtimeToUse = $AppServiceRuntime
        } else {
            $targetFrameworkMajor = Get-ProjectTargetFrameworkMajor -ProjectPath $projectPath
            $runtimeToUse = Get-LinuxDotnetRuntime -TargetMajor $targetFrameworkMajor
        }

        Invoke-Az -Arguments @('webapp','create','--resource-group',$ResourceGroupName,'--plan',$AppServicePlanName,'--name',$WebAppName,'--runtime',$runtimeToUse,'--https-only','true','--output','none') | Out-Null
    } else {
        Invoke-Az -Arguments @('webapp','create','--resource-group',$ResourceGroupName,'--plan',$AppServicePlanName,'--name',$WebAppName,'--output','none') | Out-Null
    }
} else {
    Write-Step "Web app '$WebAppName' already exists. Reusing existing app." 
}

Write-Step "Applying secure web app defaults..."
Invoke-Az -Arguments @('webapp','config','set','--resource-group',$ResourceGroupName,'--name',$WebAppName,'--always-on','true','--http20-enabled','true','--min-tls-version','1.2','--ftps-state','Disabled','--output','none') | Out-Null

Write-Step "Retrieving default host name and portal URL"
$defaultHostName = (Invoke-Az -Arguments @('webapp','show','--resource-group',$ResourceGroupName,'--name',$WebAppName,'--query','defaultHostName','--output','tsv')).Trim()
$appUrl = if ([string]::IsNullOrWhiteSpace($defaultHostName)) { "" } else { "https://$defaultHostName" }
$portalUrl = "https://portal.azure.com/#view/WebsitesExtension/WebsiteOverviewBlade/id/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$WebAppName"


Write-Step 'Publishing application (dotnet publish)'
$artifactsRoot = Join-Path $repoRoot ".artifacts\appservice\$WebAppName"
$publishDir = Join-Path $artifactsRoot 'publish'
$zipPath = Join-Path $artifactsRoot 'app.zip'
if (Test-Path $artifactsRoot) { Remove-Item $artifactsRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& dotnet publish $projectPath -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Step 'Creating deployment archive'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Step 'Applying App Service settings (in chunks)'
$chunkSize = 50
for ($i = 0; $i -lt $appSettingsArguments.Count; $i += $chunkSize) {
    $endIndex = [Math]::Min($i + $chunkSize - 1, $appSettingsArguments.Count - 1)
    $chunk = $appSettingsArguments[$i..$endIndex]
    if ($null -eq $chunk -or $chunk.Count -eq 0) { continue }
    $setArgs = @('webapp','config','appsettings','set','--resource-group',$ResourceGroupName,'--name',$WebAppName,'--settings') + $chunk
    Invoke-Az -Arguments $setArgs
}

Write-Step 'Deploying ZIP package'
Invoke-Az -Arguments @('webapp','deploy','--resource-group',$ResourceGroupName,'--name',$WebAppName,'--src-path',$zipPath,'--type','zip','--restart','true','--clean','true')

Write-Step 'Provisioning summary'
Write-Host "App URL: $appUrl" -ForegroundColor Green
Write-Host "Azure Portal: $portalUrl" -ForegroundColor Green

$result = [PSCustomObject]@{
    subscriptionId     = $SubscriptionId
    resourceGroupName  = $ResourceGroupName
    location           = $Location
    appServicePlanName = $AppServicePlanName
    webAppName         = $WebAppName
    appServiceSku      = $AppServiceSku
    appServiceRuntime  = $AppServiceRuntime
    appUrl             = $appUrl
    appServicePortalUrl= $portalUrl
}

$result | ConvertTo-Json -Depth 4
