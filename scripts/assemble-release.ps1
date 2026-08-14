[CmdletBinding()]
param(
    [string]$EvidenceDirectory = 'release-evidence',
    [string]$StagingDirectory = 'release-staging'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $EvidenceDirectory))
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $StagingDirectory))

function Get-LowercaseSha256([string]$Path) {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $stream = $null
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        $hashBytes = $sha256.ComputeHash($stream)
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
        $sha256.Dispose()
    }
    return [System.BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
}

if (-not $stagingRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release staging must remain inside the repository checkout: $stagingRoot"
}
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Could not resolve the release commit.' }
if ($env:GITHUB_SHA -and $env:GITHUB_SHA -cne $commit) { throw "GITHUB_SHA $env:GITHUB_SHA does not match checked-out commit $commit." }
$packageVersion = [string]$env:WST_RELEASE_VERSION
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$') { throw "WST_RELEASE_VERSION must be numeric major.minor.patch text; got '$packageVersion'." }

& (Join-Path $PSScriptRoot 'verify-exchange-package.ps1') -SourceCommit $commit -ExpectedVersion $packageVersion -JsonOutputPath (Join-Path $EvidenceDirectory 'exchange-package.json')
if ($LASTEXITCODE -ne 0) { throw 'Exchange Squirrel.Windows verification failed during release assembly.' }

$wpfInstaller = Join-Path $repoRoot "Windows-Server-Tools\Windows-Server-Tools\bin\Installer\WindowsServerTools-Setup-$commit.exe"
$wpfVersionFile = Join-Path $repoRoot 'Windows-Server-Tools\Windows-Server-Tools\bin\Installer\package-version.txt'
$squirrelRoot = Join-Path $repoRoot 'Windows-Server-Tools\Exchange-Auto-Installer\dist\squirrel-windows'
if (-not (Test-Path -LiteralPath $wpfInstaller -PathType Leaf)) { throw "The WPF installer is missing: $wpfInstaller" }
if (-not (Test-Path -LiteralPath $wpfVersionFile -PathType Leaf)) { throw "The WPF installer version evidence is missing: $wpfVersionFile" }
if ((Get-Content -LiteralPath $wpfVersionFile -Raw).Trim() -cne $packageVersion) { throw 'The WPF installer does not carry the workflow package version.' }
if (-not (Test-Path -LiteralPath $squirrelRoot -PathType Container)) { throw "The Squirrel.Windows output is missing: $squirrelRoot" }

$exchangeSetups = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-Setup.exe' })
$fullPackages = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-full.nupkg' })
$deltaPackages = @(Get-ChildItem -LiteralPath $squirrelRoot -File | Where-Object { $_.Name -like '*-delta.nupkg' })
$releaseIndex = Join-Path $squirrelRoot 'RELEASES'
if ($exchangeSetups.Count -ne 1) { throw "Expected exactly one Exchange Squirrel setup executable; found $($exchangeSetups.Count)." }
if ($fullPackages.Count -ne 1) { throw "Expected exactly one Exchange full Squirrel package; found $($fullPackages.Count)." }
if (-not (Test-Path -LiteralPath $releaseIndex -PathType Leaf)) { throw 'The Exchange Squirrel RELEASES index is missing.' }
$releaseIndexText = Get-Content -LiteralPath $releaseIndex -Raw
if (-not $releaseIndexText.Contains($fullPackages[0].Name, [StringComparison]::Ordinal)) {
    throw "The Squirrel RELEASES index does not reference $($fullPackages[0].Name)."
}

& (Join-Path $PSScriptRoot 'verify-unsigned-pe.ps1') -Path @($wpfInstaller, $exchangeSetups[0].FullName)
if ($LASTEXITCODE -ne 0) { throw 'Unsigned PE verification failed.' }

$releaseArtifacts = @(
    [ordered]@{ role = 'primary-wpf-installer'; source = $wpfInstaller },
    [ordered]@{ role = 'exchange-squirrel-setup'; source = $exchangeSetups[0].FullName },
    [ordered]@{ role = 'exchange-squirrel-index'; source = $releaseIndex },
    [ordered]@{ role = 'exchange-squirrel-full-package'; source = $fullPackages[0].FullName }
)
foreach ($delta in $deltaPackages) {
    $releaseArtifacts += [ordered]@{ role = 'exchange-squirrel-delta-package'; source = $delta.FullName }
}

$manifestArtifacts = @()
foreach ($artifact in $releaseArtifacts) {
    $source = [IO.Path]::GetFullPath([string]$artifact.source)
    $destination = Join-Path $stagingRoot ([IO.Path]::GetFileName($source))
    Copy-Item -LiteralPath $source -Destination $destination
    $item = Get-Item -LiteralPath $destination
    if ($item.Length -le 0) { throw "A staged release artifact is empty: $destination" }
    $manifestArtifacts += [ordered]@{
        role = $artifact.role
        name = $item.Name
        bytes = $item.Length
        sha256 = Get-LowercaseSha256 -Path $item.FullName
        unsigned = $item.Extension -ieq '.exe'
    }
}

$requiredEvidence = @('line-count.md', 'line-count.json', 'dim-sum.json', 'exchange-package.json')
foreach ($name in $requiredEvidence) {
    $source = Join-Path $evidenceRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required release evidence is missing: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stagingRoot $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-dependencies.json') -Destination (Join-Path $stagingRoot 'release-dependencies.json')

$runContext = [ordered]@{
    schemaVersion = 1
    repository = $env:GITHUB_REPOSITORY
    commit = $commit
    ref = $env:GITHUB_REF
    event = $env:GITHUB_EVENT_NAME
    runId = $env:GITHUB_RUN_ID
    runNumber = $env:GITHUB_RUN_NUMBER
    runAttempt = $env:GITHUB_RUN_ATTEMPT
    job = $env:GITHUB_JOB
    runnerName = $env:RUNNER_NAME
    runnerOs = $env:RUNNER_OS
    runnerArch = $env:RUNNER_ARCH
    signingPolicy = 'No code signing. Setup executables were verified to have no PE certificate table.'
}
[IO.File]::WriteAllText((Join-Path $stagingRoot 'run-context.json'), ($runContext | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    schemaVersion = 1
    commit = $commit
    packageVersion = $packageVersion
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    artifacts = $manifestArtifacts
}
[IO.File]::WriteAllText((Join-Path $stagingRoot 'artifact-manifest.json'), ($manifest | ConvertTo-Json -Depth 7), [Text.UTF8Encoding]::new($false))

$hashLines = @()
foreach ($file in Get-ChildItem -LiteralPath $stagingRoot -File | Sort-Object Name) {
    if ($file.Name -eq 'SHA256SUMS.txt') { continue }
    $hash = Get-LowercaseSha256 -Path $file.FullName
    $hashLines += "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $stagingRoot 'SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))

Write-Output "Staged $($manifestArtifacts.Count) install/update artifacts from commit $commit."
Get-ChildItem -LiteralPath $stagingRoot -File | Sort-Object Name | Select-Object Name, Length
