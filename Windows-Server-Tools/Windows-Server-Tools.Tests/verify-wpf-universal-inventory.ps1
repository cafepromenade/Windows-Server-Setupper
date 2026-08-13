[CmdletBinding()]
param(
    [switch]$Release,
    [ValidatePattern('^WPF-[0-9]{2}$')]
    [string]$NegativeProbe
)

$ErrorActionPreference = 'Stop'

$expectedIds = 1..50 | ForEach-Object { 'WPF-{0:D2}' -f $_ }
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$inventoryPath = Join-Path (Split-Path -Parent $scriptRoot) 'docs\completeness\wpf-universal-feature-inventory.md'

if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
    throw "WPF completeness inventory is missing: $inventoryPath"
}

$content = Get-Content -LiteralPath $inventoryPath -Raw
if ($NegativeProbe) {
    $content = $content.Replace("<!-- $NegativeProbe -->", '<!-- deliberately removed by negative probe -->')
}

$matches = [regex]::Matches($content, '<!-- (WPF-[0-9]{2}) -->')
$actualIds = @($matches | ForEach-Object { $_.Groups[1].Value })
$duplicateIds = @($actualIds | Group-Object | Where-Object Count -ne 1 | ForEach-Object Name)
$missingIds = @($expectedIds | Where-Object { $_ -notin $actualIds })
$unexpectedIds = @($actualIds | Where-Object { $_ -notin $expectedIds } | Select-Object -Unique)

if ($duplicateIds.Count -gt 0 -or $missingIds.Count -gt 0 -or $unexpectedIds.Count -gt 0) {
    throw ("WPF completeness inventory identity failure. Missing=[{0}] Duplicate=[{1}] Unexpected=[{2}]" -f
        ($missingIds -join ', '), ($duplicateIds -join ', '), ($unexpectedIds -join ', '))
}

$rows = @{}
foreach ($line in ($content -split "`r?`n")) {
    if ($line -match '^\| <!-- (WPF-[0-9]{2}) --> .* \| (READY|PARTIAL|MISSING|DOCUMENTED-NOT-APPLICABLE) \|$') {
        $cells = @($line.Split('|'))
        if ($cells.Count -ne 12) {
            throw "Inventory row $($Matches[1]) does not contain the ten required evidence columns."
        }

        $rows[$Matches[1]] = $Matches[2]
    }
}

$unparsedIds = @($expectedIds | Where-Object { -not $rows.ContainsKey($_) })
if ($unparsedIds.Count -gt 0) {
    throw "WPF completeness inventory rows are malformed or missing a recognized verdict: $($unparsedIds -join ', ')"
}

if ($Release) {
    $blockingRows = @($expectedIds | Where-Object { $rows[$_] -in @('PARTIAL', 'MISSING') })
    if ($blockingRows.Count -gt 0) {
        throw "WPF release completeness is blocked by $($blockingRows.Count) rows: $($blockingRows -join ', ')"
    }
}

Write-Output "PASS: WPF completeness inventory contains $($expectedIds.Count) exact rows."
if (-not $Release) {
    $summary = $rows.GetEnumerator() | Group-Object Value | Sort-Object Name
    foreach ($group in $summary) {
        Write-Output ("{0}: {1}" -f $group.Name, $group.Count)
    }
}
