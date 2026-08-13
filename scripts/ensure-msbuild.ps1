[CmdletBinding()]
param(
    [string]$ToolchainRoot = (Join-Path $env:LOCALAPPDATA 'WindowsServerTools\toolchain')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '17.14.37'
$installerUri = 'https://download.visualstudio.microsoft.com/download/pr/f7f5ecbc-83ca-4cf0-bdb2-aaf70efb6d97/e0b8ea16494b4a79c68da26773131562aefecc8d87f1923c24d579c7a72e0575/vs_BuildTools.exe'
$installerSha256 = 'e0b8ea16494b4a79c68da26773131562aefecc8d87f1923c24d579c7a72e0575'
$installRoot = [IO.Path]::GetFullPath((Join-Path $ToolchainRoot "vs-buildtools-$version"))
$components = @(
    'Microsoft.VisualStudio.Workload.MSBuildTools',
    'Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools',
    'Microsoft.Net.Component.4.7.2.TargetingPack',
    'Microsoft.Net.Component.4.7.2.SDK'
)

function Find-CompatibleMsBuild {
    $localCandidate = Join-Path $installRoot 'MSBuild\Current\Bin\MSBuild.exe'
    if (Test-Path -LiteralPath $localCandidate -PathType Leaf) { return [IO.Path]::GetFullPath($localCandidate) }

    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    )
    foreach ($vswhere in $vswhereCandidates) {
        if (-not $vswhere -or -not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { continue }
        $matches = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null)
        if ($LASTEXITCODE -eq 0) {
            foreach ($match in $matches) {
                if ($match -and (Test-Path -LiteralPath $match -PathType Leaf)) { return [IO.Path]::GetFullPath($match) }
            }
        }
    }
    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) { return [IO.Path]::GetFullPath($command.Source) }
    return $null
}

function Get-InstallerArguments([string]$Path) {
    $arguments = @('--quiet', '--wait', '--norestart', '--nocache', '--installPath', "`"$Path`"")
    foreach ($component in $components) { $arguments += @('--add', $component) }
    $arguments += '--includeRecommended'
    return $arguments
}

$msbuild = Find-CompatibleMsBuild
if ($msbuild) {
    Write-Output $msbuild
    exit 0
}

$winget = Get-Command winget.exe -ErrorAction SilentlyContinue
if ($winget) {
    $override = (Get-InstallerArguments $installRoot) -join ' '
    $wingetArguments = @(
        'install', '--id', 'Microsoft.VisualStudio.2022.BuildTools', '--exact', '--version', $version,
        '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity',
        '--override', "`"$override`""
    )
    $process = Start-Process -FilePath $winget.Source -ArgumentList $wingetArguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -eq 0) {
        $msbuild = Find-CompatibleMsBuild
        if ($msbuild) {
            Write-Output $msbuild
            exit 0
        }
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("wst-buildtools-$version-" + [Guid]::NewGuid().ToString('N'))
$installerPath = Join-Path $scratch 'vs_BuildTools.exe'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
try {
    Invoke-WebRequest -UseBasicParsing -Uri $installerUri -OutFile $installerPath
    $actualSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -cne $installerSha256) {
        throw "Microsoft Build Tools $version SHA-256 mismatch from $installerUri."
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $installRoot) -Force | Out-Null
    $process = Start-Process -FilePath $installerPath -ArgumentList (Get-InstallerArguments $installRoot) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Official Microsoft Build Tools $version installer exited with code $($process.ExitCode)." }
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if ($resolvedScratch.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedScratch)) {
        [IO.Directory]::Delete($resolvedScratch, $true)
    }
}

$msbuild = Find-CompatibleMsBuild
if (-not $msbuild) {
    throw "Microsoft Build Tools $version was unavailable after trying installed instances, pinned winget, and $installerUri."
}
Write-Output $msbuild
