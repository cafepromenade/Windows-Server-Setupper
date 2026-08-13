[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$JobStatus,
    [string]$OutputDirectory = '.release-artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
if (-not $outputRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Safe output collection must remain inside the checkout: $outputRoot"
}
if (Test-Path -LiteralPath $outputRoot) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$context = [ordered]@{
    schemaVersion = 1
    collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    jobStatus = $JobStatus
    repository = $env:GITHUB_REPOSITORY
    commit = $env:GITHUB_SHA
    ref = $env:GITHUB_REF
    runId = $env:GITHUB_RUN_ID
    runAttempt = $env:GITHUB_RUN_ATTEMPT
    job = $env:GITHUB_JOB
    runnerName = $env:RUNNER_NAME
    runnerOs = $env:RUNNER_OS
    runnerArch = $env:RUNNER_ARCH
}
[IO.File]::WriteAllText((Join-Path $outputRoot 'run-context.json'), ($context | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

$safeDirectories = @('release-evidence', 'release-staging')
foreach ($relative in $safeDirectories) {
    $source = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { continue }
    $destination = Join-Path $outputRoot $relative
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name)
    }
}

$knownArtifacts = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Windows-Server-Tools\Windows-Server-Tools\bin\Installer') -File -ErrorAction SilentlyContinue,
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Windows-Server-Tools\Exchange-Auto-Installer\dist\squirrel-windows') -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.exe', '.nupkg') -or $_.Name -eq 'RELEASES' }
) | Where-Object { $_ }
if ($knownArtifacts.Count -gt 0) {
    $artifactOutput = Join-Path $outputRoot 'partial-build-outputs'
    New-Item -ItemType Directory -Path $artifactOutput -Force | Out-Null
    foreach ($artifact in $knownArtifacts) {
        Copy-Item -LiteralPath $artifact.FullName -Destination (Join-Path $artifactOutput $artifact.Name)
    }
}

$packageLog = Join-Path $repoRoot 'Windows-Server-Tools\Exchange-Auto-Installer\dist\package.log'
if (Test-Path -LiteralPath $packageLog -PathType Leaf) {
    $logOutput = Join-Path $outputRoot 'logs'
    New-Item -ItemType Directory -Path $logOutput -Force | Out-Null
    Copy-Item -LiteralPath $packageLog -Destination (Join-Path $logOutput 'exchange-package.log')
}

Write-Output "Collected safe outputs for job state '$JobStatus' at $outputRoot."
