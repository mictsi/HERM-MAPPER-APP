param(
    [Parameter(Mandatory = $true)]
    [string]$CoveragePath,

    [Parameter(Mandatory = $false)]
    [double]$Threshold = 90
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoveragePath)) {
    throw "Coverage report not found at '$CoveragePath'."
}

[xml]$coverage = Get-Content -LiteralPath $CoveragePath

$excludedFiles = @(
    'Services\DatabaseInitializer.cs',
    'Services\SampleRelationshipImportService.cs',
    'Services\TrmWorkbookImportService.cs'
)

$lineStates = @{}

foreach ($class in @($coverage.coverage.packages.package.classes.class)) {
    $file = [string]$class.filename
    if ([string]::IsNullOrWhiteSpace($file)) {
        continue
    }

    $isIncluded =
        $file -eq 'Program.cs' -or
        $file.StartsWith('Controllers\', [System.StringComparison]::Ordinal) -or
        $file.StartsWith('Services\', [System.StringComparison]::Ordinal) -or
        $file.StartsWith('Configuration\', [System.StringComparison]::Ordinal) -or
        $file.StartsWith('Data\', [System.StringComparison]::Ordinal) -or
        $file.StartsWith('Infrastructure\', [System.StringComparison]::Ordinal)

    if (-not $isIncluded) {
        continue
    }

    if ($excludedFiles -contains $file) {
        continue
    }

    foreach ($line in @($class.lines.line)) {
        $key = "$file|$($line.number)"
        $hits = [int]$line.hits

        if ($lineStates.ContainsKey($key)) {
            if ($hits -gt $lineStates[$key]) {
                $lineStates[$key] = $hits
            }
        }
        else {
            $lineStates[$key] = $hits
        }
    }
}

$validLineCount = $lineStates.Count
if ($validLineCount -eq 0) {
    throw 'No expanded application coverage data was found in the generated report.'
}

$coveredLineCount = @($lineStates.GetEnumerator() | Where-Object { $_.Value -gt 0 }).Count
$coveragePercent = [math]::Round(($coveredLineCount / $validLineCount) * 100, 2)

Write-Host ("Expanded app line coverage: {0}% ({1}/{2})" -f $coveragePercent, $coveredLineCount, $validLineCount)

if ($coveragePercent -lt $Threshold) {
    $uncoveredByFile = $lineStates.GetEnumerator() |
        Where-Object { $_.Value -eq 0 } |
        Group-Object { ($_.Key -split '\|')[0] } |
        Sort-Object Count -Descending |
        Select-Object -First 10

    if ($uncoveredByFile.Count -gt 0) {
        Write-Host 'Top uncovered files:'
        foreach ($group in $uncoveredByFile) {
            Write-Host ("  {0}: {1}" -f $group.Name, $group.Count)
        }
    }

    throw ("Expanded app line coverage {0}% is below the required {1}% threshold." -f $coveragePercent, $Threshold)
}