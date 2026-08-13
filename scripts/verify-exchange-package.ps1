[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceCommit,
    [string]$ExpectedVersion,
    [string]$OutputRoot = 'Windows-Server-Tools\Exchange-Auto-Installer\dist',
    [string]$JsonOutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))
$squirrelRoot = Join-Path $distRoot 'squirrel-windows'
$unpackedRoot = Join-Path $distRoot 'win-unpacked'
if ($SourceCommit -notmatch '^[0-9a-f]{40}$') { throw "SourceCommit is not an exact Git SHA: $SourceCommit" }
if (-not (Test-Path -LiteralPath $squirrelRoot -PathType Container)) { throw "Squirrel.Windows output is missing: $squirrelRoot" }
if (-not (Test-Path -LiteralPath $unpackedRoot -PathType Container)) { throw "Unpacked application output is missing: $unpackedRoot" }

$commitEvidencePath = Join-Path $distRoot 'source-commit.txt'
$versionEvidencePath = Join-Path $distRoot 'package-version.txt'
if (-not (Test-Path -LiteralPath $commitEvidencePath -PathType Leaf)) { throw 'Exchange package source-commit evidence is missing.' }
if (-not (Test-Path -LiteralPath $versionEvidencePath -PathType Leaf)) { throw 'Exchange package version evidence is missing.' }
$recordedCommit = (Get-Content -LiteralPath $commitEvidencePath -Raw).Trim()
if ($recordedCommit -cne $SourceCommit) { throw "Exchange package provenance $recordedCommit does not match $SourceCommit." }
$version = (Get-Content -LiteralPath $versionEvidencePath -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Recorded Exchange package version is invalid: $version" }
if ($ExpectedVersion -and $version -cne $ExpectedVersion) { throw "Recorded Exchange package version $version does not match expected shared version $ExpectedVersion." }

$setups = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-Setup.exe' })
$fullPackages = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-full.nupkg' })
$deltaPackages = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-delta.nupkg' })
if ($setups.Count -ne 1) { throw "Expected exactly one Squirrel setup executable; found $($setups.Count)." }
if ($fullPackages.Count -ne 1) { throw "Expected exactly one full Squirrel package; found $($fullPackages.Count)." }
if ($setups[0].Length -lt 102400) { throw "Squirrel setup is unexpectedly small: $($setups[0].Length) bytes." }

$unpackedExecutables = @(Get-ChildItem -LiteralPath $unpackedRoot -File -Filter '*.exe' | Where-Object { $_.Name -notmatch '(?i)(elevate|squirrel|update|_ExecutionStub\.exe$)' })
if ($unpackedExecutables.Count -ne 1) { throw "Expected exactly one unpacked application executable; found $($unpackedExecutables.Count)." }
$appAsar = Join-Path $unpackedRoot 'resources\app.asar'
if (-not (Test-Path -LiteralPath $appAsar -PathType Leaf)) { throw 'The unpacked Exchange application is missing resources/app.asar.' }

$releaseIndex = Join-Path $squirrelRoot 'RELEASES'
if (-not (Test-Path -LiteralPath $releaseIndex -PathType Leaf)) { throw 'The Squirrel RELEASES index is missing.' }
$indexEntries = @()
foreach ($line in Get-Content -LiteralPath $releaseIndex) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '^([0-9A-Fa-f]{40})\s+(\S+)\s+(\d+)$') { throw "Malformed Squirrel RELEASES line: $line" }
    $expectedSha1 = $Matches[1].ToLowerInvariant()
    $name = $Matches[2]
    $expectedBytes = [int64]$Matches[3]
    $path = Join-Path $squirrelRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "RELEASES references a missing package: $name" }
    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne $expectedBytes) { throw "RELEASES size mismatch for ${name}: index=$expectedBytes file=$($item.Length)" }
    $actualSha1 = (Get-FileHash -LiteralPath $path -Algorithm SHA1).Hash.ToLowerInvariant()
    if ($actualSha1 -cne $expectedSha1) { throw "RELEASES SHA-1 mismatch for $name" }
    $indexEntries += [ordered]@{ name = $name; bytes = $item.Length; sha1 = $actualSha1 }
}
if ($indexEntries.Count -eq 0) { throw 'The Squirrel RELEASES index contains no package entries.' }
if ($indexEntries.name -notcontains $fullPackages[0].Name) { throw "RELEASES does not reference $($fullPackages[0].Name)." }

& (Join-Path $PSScriptRoot 'verify-unsigned-pe.ps1') -Path @($setups[0].FullName, $unpackedExecutables[0].FullName)

$artifacts = @($setups[0], (Get-Item -LiteralPath $releaseIndex), $fullPackages[0]) + $deltaPackages
$artifactRows = foreach ($artifact in $artifacts) {
    [ordered]@{
        name = $artifact.Name
        bytes = $artifact.Length
        sha256 = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$result = [ordered]@{
    schemaVersion = 1
    sourceCommit = $SourceCommit
    packageVersion = $version
    unsigned = $true
    setup = $setups[0].Name
    unpackedExecutable = $unpackedExecutables[0].Name
    fullPackage = $fullPackages[0].Name
    deltaPackages = @($deltaPackages | ForEach-Object { $_.Name })
    releaseIndex = $indexEntries
    artifacts = @($artifactRows)
}
if ($JsonOutputPath) {
    $resolvedJson = [IO.Path]::GetFullPath((Join-Path $repoRoot $JsonOutputPath))
    New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedJson) -Force | Out-Null
    [IO.File]::WriteAllText($resolvedJson, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
}

Write-Output "Verified unsigned Exchange Squirrel.Windows package version $version from commit $SourceCommit."
$artifactRows | Format-Table -AutoSize
