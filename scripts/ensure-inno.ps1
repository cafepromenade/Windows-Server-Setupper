[CmdletBinding()]
param(
    [string]$ToolchainRoot = (Join-Path $env:LOCALAPPDATA 'WindowsServerTools\toolchain')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '6.7.3'
$installerUri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$installerSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$installRoot = [IO.Path]::GetFullPath((Join-Path $ToolchainRoot "inno-setup-$version"))

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

function Find-CompatibleCompiler {
    $candidates = @(
        (Join-Path $installRoot 'ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf) -and (Get-Item -LiteralPath $candidate).Length -gt 0) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) { return [IO.Path]::GetFullPath($command.Source) }
    return $null
}

$compiler = Find-CompatibleCompiler
if ($compiler) {
    Write-Output $compiler
    exit 0
}

$winget = Get-Command winget.exe -ErrorAction SilentlyContinue
if ($winget) {
    $wingetArguments = @(
        'install', '--id', 'JRSoftware.InnoSetup', '--exact', '--version', $version,
        '--scope', 'user', '--silent', '--accept-package-agreements', '--accept-source-agreements',
        '--disable-interactivity'
    )
    $process = Start-Process -FilePath $winget.Source -ArgumentList $wingetArguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -eq 0) {
        $compiler = Find-CompatibleCompiler
        if ($compiler) {
            Write-Output $compiler
            exit 0
        }
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("wst-inno-$version-" + [Guid]::NewGuid().ToString('N'))
$installerPath = Join-Path $scratch 'innosetup.exe'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
try {
    Invoke-WebRequest -UseBasicParsing -Uri $installerUri -OutFile $installerPath
    $actualSha256 = Get-LowercaseSha256 -Path $installerPath
    if ($actualSha256 -cne $installerSha256) {
        throw "Inno Setup $version SHA-256 mismatch from $installerUri."
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $installRoot) -Force | Out-Null
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$installRoot`"")
    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Official Inno Setup $version installer exited with code $($process.ExitCode)." }
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if ($resolvedScratch.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedScratch)) {
        [IO.Directory]::Delete($resolvedScratch, $true)
    }
}

$compiler = Find-CompatibleCompiler
if (-not $compiler) {
    throw "Inno Setup $version was unavailable after trying compatible installed copies, pinned winget, and $installerUri."
}
Write-Output $compiler
