[CmdletBinding()]
param(
    [string]$VersionFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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

if ([string]::IsNullOrWhiteSpace($VersionFile)) {
    $VersionFile = Join-Path (Split-Path -Parent $PSScriptRoot) '.node-version'
}

if (-not (Test-Path -LiteralPath $VersionFile -PathType Leaf)) {
    throw "Node version file is missing: $VersionFile"
}

$version = (Get-Content -LiteralPath $VersionFile -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Node version must be an exact semantic version: $version"
}

$installedNode = Get-Command node.exe -ErrorAction SilentlyContinue
if ($installedNode) {
    $installedVersion = (& $installedNode.Source --version).TrimStart('v')
    if ($LASTEXITCODE -eq 0 -and $installedVersion -ceq $version) {
        Split-Path -Parent $installedNode.Source
        exit 0
    }
}

if (-not $env:LOCALAPPDATA) {
    throw 'LOCALAPPDATA is unavailable, so a user-scoped Node.js toolchain cannot be installed.'
}

$toolchainRoot = Join-Path $env:LOCALAPPDATA 'WindowsServerTools\toolchain'
$target = Join-Path $toolchainRoot "node-v$version-win-x64"
$nodePath = Join-Path $target 'node.exe'
$npmPath = Join-Path $target 'npm.cmd'
if ((Test-Path -LiteralPath $nodePath -PathType Leaf) -and (Test-Path -LiteralPath $npmPath -PathType Leaf)) {
    $cachedVersion = (& $nodePath --version).TrimStart('v')
    if ($LASTEXITCODE -eq 0 -and $cachedVersion -ceq $version) {
        $target
        exit 0
    }
}

$baseUri = "https://nodejs.org/dist/v$version"
$archiveName = "node-v$version-win-x64.zip"
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("wst-node-" + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $scratch $archiveName
$expanded = Join-Path $scratch 'expanded'
$checksums = Join-Path $scratch 'SHASUMS256.txt'

New-Item -ItemType Directory -Path $expanded -Force | Out-Null
try {
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUri/SHASUMS256.txt" -OutFile $checksums
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUri/$archiveName" -OutFile $archive

    $checksumLine = Get-Content -LiteralPath $checksums | Where-Object { $_ -match ("^[0-9a-fA-F]{64}\s+" + [regex]::Escape($archiveName) + '$') } | Select-Object -First 1
    if (-not $checksumLine) {
        throw "The official checksum list does not contain $archiveName."
    }
    $expected = ($checksumLine -split '\s+')[0].ToLowerInvariant()
    $actual = Get-LowercaseSha256 -Path $archive
    if ($actual -cne $expected) {
        throw "Node.js archive SHA-256 mismatch for $archiveName."
    }

    Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
    $staged = Join-Path $expanded "node-v$version-win-x64"
    if (-not (Test-Path -LiteralPath (Join-Path $staged 'node.exe') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $staged 'npm.cmd') -PathType Leaf)) {
        throw 'The verified Node.js archive did not contain node.exe and npm.cmd.'
    }

    New-Item -ItemType Directory -Path $toolchainRoot -Force | Out-Null
    if (Test-Path -LiteralPath $target) {
        $preserved = "$target.invalid-$([Guid]::NewGuid().ToString('N'))"
        Move-Item -LiteralPath $target -Destination $preserved
    }
    Move-Item -LiteralPath $staged -Destination $target
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $nodePath -PathType Leaf) -or -not (Test-Path -LiteralPath $npmPath -PathType Leaf)) {
    throw 'The Node.js toolchain was not present after verified extraction.'
}

$target
