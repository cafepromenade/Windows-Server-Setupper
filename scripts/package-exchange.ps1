[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$NodeHome,
    [Parameter(Mandatory)]
    [string]$SourceCommit,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourceRoot = Join-Path $repoRoot 'Windows-Server-Tools\Exchange-Auto-Installer'
$nodePath = Join-Path $NodeHome 'node.exe'
$npmPath = Join-Path $NodeHome 'npm.cmd'
if (-not (Test-Path -LiteralPath $nodePath -PathType Leaf) -or -not (Test-Path -LiteralPath $npmPath -PathType Leaf)) {
    throw "The requested Node.js toolchain is incomplete: $NodeHome"
}
if ($SourceCommit -notmatch '^[0-9a-f]{40}$') { throw "SourceCommit is not an exact Git SHA: $SourceCommit" }
if (-not $Version) {
    $Version = [string]((Get-Content -LiteralPath (Join-Path $sourceRoot 'package.json') -Raw | ConvertFrom-Json).version)
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Exchange package version is not valid semantic version text: $Version" }

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("wst-exchange-package-" + [Guid]::NewGuid().ToString('N'))
$stagedRoot = Join-Path $scratch 'Exchange-Auto-Installer'
$packageLog = Join-Path $scratch 'package.log'
$destinationDist = Join-Path $sourceRoot 'dist'

New-Item -ItemType Directory -Path $stagedRoot -Force | Out-Null
try {
    Get-ChildItem -LiteralPath $sourceRoot -Force | Where-Object { $_.Name -notin @('node_modules', 'dist') } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stagedRoot $_.Name) -Recurse -Force
    }

    Push-Location $stagedRoot
    try {
        $env:WST_STAGED_VERSION = $Version
        $versionScript = @'
const fs = require('node:fs');
for (const name of ['package.json', 'package-lock.json']) {
  const value = JSON.parse(fs.readFileSync(name, 'utf8'));
  value.version = process.env.WST_STAGED_VERSION;
  if (name === 'package-lock.json' && value.packages && value.packages['']) {
    value.packages[''].version = process.env.WST_STAGED_VERSION;
  }
  fs.writeFileSync(name, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}
'@
        & $nodePath -e $versionScript
        if ($LASTEXITCODE -ne 0) { throw 'Could not apply the task-owned package version in the isolated staging copy.' }

        & $npmPath ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed in the isolated Exchange package copy with exit code $LASTEXITCODE." }

        foreach ($name in @(
            'CSC_LINK', 'CSC_KEY_PASSWORD', 'CSC_NAME', 'WIN_CSC_LINK', 'WIN_CSC_KEY_PASSWORD',
            'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET',
            'AZURE_CODE_SIGNING_ACCOUNT_NAME', 'AZURE_CERTIFICATE_PROFILE_NAME'
        )) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        }
        $env:CSC_IDENTITY_AUTO_DISCOVERY = 'false'

        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $npmPath run package 2>&1 | Tee-Object -LiteralPath $packageLog
            $packageExit = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        if ($packageExit -ne 0) { throw "Exchange Squirrel.Windows packaging failed with exit code $packageExit." }
    }
    finally {
        Pop-Location
    }

    $packageLogText = Get-Content -LiteralPath $packageLog -Raw
    if ($packageLogText -match '(?im)\bsigntool(?:\.exe)?\b|\bsigning\s+(?:file|executable|artifact)\b|\bcertificate\s+(?:subject|thumbprint|discovery)\b') {
        throw 'The Exchange packager log indicates a signer, signtool, certificate, or signing invocation.'
    }

    $stagedDist = Join-Path $stagedRoot 'dist'
    $stagedSquirrel = Join-Path $stagedDist 'squirrel-windows'
    $stagedUnpacked = Join-Path $stagedDist 'win-unpacked'
    if (-not (Test-Path -LiteralPath $stagedSquirrel -PathType Container)) { throw 'The isolated package did not produce dist/squirrel-windows.' }
    if (-not (Test-Path -LiteralPath $stagedUnpacked -PathType Container)) { throw 'The isolated package did not produce dist/win-unpacked.' }

    $expectedDestination = [IO.Path]::GetFullPath($destinationDist)
    $expectedParent = [IO.Path]::GetFullPath($sourceRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $expectedDestination.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($expectedDestination) -cne 'dist') {
        throw "Refusing to replace unexpected package output: $expectedDestination"
    }
    if (Test-Path -LiteralPath $expectedDestination) { Remove-Item -LiteralPath $expectedDestination -Recurse -Force }
    New-Item -ItemType Directory -Path $expectedDestination -Force | Out-Null
    Copy-Item -LiteralPath $stagedSquirrel -Destination (Join-Path $expectedDestination 'squirrel-windows') -Recurse
    Copy-Item -LiteralPath $stagedUnpacked -Destination (Join-Path $expectedDestination 'win-unpacked') -Recurse
    [IO.File]::WriteAllText((Join-Path $expectedDestination 'source-commit.txt'), "$SourceCommit`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $expectedDestination 'package-version.txt'), "$Version`n", [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $packageLog -Destination (Join-Path $expectedDestination 'package.log')
}
finally {
    Remove-Item Env:WST_STAGED_VERSION -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Output "Exchange Squirrel.Windows package version $Version was produced from commit $SourceCommit."
Write-Output "Output: $destinationDist"
