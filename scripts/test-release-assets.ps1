[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$fixtureRoot = Join-Path $repoRoot '.release-contract-fixtures'
$commit = '0123456789abcdef0123456789abcdef01234567'

function Write-MinimalUnsignedPe([string]$Path) {
    $bytes = [byte[]]::new(131072)
    [BitConverter]::GetBytes([uint16]0x5A4D).CopyTo($bytes, 0)
    [BitConverter]::GetBytes([uint32]0x80).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x80)
    [BitConverter]::GetBytes([uint16]0x14C).CopyTo($bytes, 0x84)
    [BitConverter]::GetBytes([uint16]0).CopyTo($bytes, 0x86)
    [BitConverter]::GetBytes([uint16]0xE0).CopyTo($bytes, 0x94)
    [BitConverter]::GetBytes([uint16]0x10B).CopyTo($bytes, 0x98)
    [BitConverter]::GetBytes([uint32]16).CopyTo($bytes, 0x98 + 92)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-Fixture([string]$Name) {
    $dist = Join-Path $fixtureRoot $Name
    $squirrel = Join-Path $dist 'squirrel-windows'
    $unpacked = Join-Path $dist 'win-unpacked'
    New-Item -ItemType Directory -Path (Join-Path $unpacked 'resources') -Force | Out-Null
    New-Item -ItemType Directory -Path $squirrel -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $dist 'source-commit.txt'), "$commit`n")
    [IO.File]::WriteAllText((Join-Path $dist 'package-version.txt'), "1.2.3`n")
    Write-MinimalUnsignedPe (Join-Path $squirrel 'ExchangeAutoInstaller-1.2.3-x64-Setup.exe')
    $packagePath = Join-Path $squirrel 'exchange_auto_installer-1.2.3-full.nupkg'
    [IO.File]::WriteAllText($packagePath, 'fixture package bytes')
    $package = Get-Item -LiteralPath $packagePath
    $sha1 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA1).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $squirrel 'RELEASES'), "$sha1 $($package.Name) $($package.Length)`n")
    Write-MinimalUnsignedPe (Join-Path $unpacked 'Exchange Auto Installer.exe')
    [IO.File]::WriteAllText((Join-Path $unpacked 'resources\app.asar'), 'fixture asar')
    return $dist
}

function Expect-Failure([string]$Name, [scriptblock]$Mutate, [string]$ExpectedMessage) {
    $dist = New-Fixture $Name
    & $Mutate $dist
    $relative = [IO.Path]::GetRelativePath($repoRoot, $dist)
    try {
        & (Join-Path $PSScriptRoot 'verify-exchange-package.ps1') -SourceCommit $commit -ExpectedVersion '1.2.3' -OutputRoot $relative *> $null
        throw "Fixture '$Name' unexpectedly passed."
    }
    catch {
        if ($_.Exception.Message -notmatch $ExpectedMessage) {
            throw "Fixture '$Name' failed for the wrong reason: $($_.Exception.Message)"
        }
        Write-Output "EXPECTED RED: $Name -> $($_.Exception.Message)"
    }
}

if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
try {
    $baseline = New-Fixture 'valid-baseline'
    $baselineRelative = [IO.Path]::GetRelativePath($repoRoot, $baseline)
    & (Join-Path $PSScriptRoot 'verify-exchange-package.ps1') -SourceCommit $commit -ExpectedVersion '1.2.3' -OutputRoot $baselineRelative *> $null
    Write-Output 'PASS: valid unsigned Squirrel.Windows fixture was accepted'
    Expect-Failure 'missing-setup' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'squirrel-windows\ExchangeAutoInstaller-1.2.3-x64-Setup.exe') } 'exactly one Squirrel setup'
    Expect-Failure 'missing-releases' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'squirrel-windows\RELEASES') } 'RELEASES index is missing'
    Expect-Failure 'missing-full-package' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'squirrel-windows\exchange_auto_installer-1.2.3-full.nupkg') } 'exactly one full Squirrel package'
    Expect-Failure 'missing-unpacked-app' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'win-unpacked\Exchange Auto Installer.exe') } 'exactly one unpacked application executable'
    Expect-Failure 'missing-asar' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'win-unpacked\resources\app.asar') } 'missing resources/app.asar'
    Expect-Failure 'missing-provenance' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'source-commit.txt') } 'source-commit evidence is missing'
    Expect-Failure 'missing-package-version' { param($dist) Remove-Item -LiteralPath (Join-Path $dist 'package-version.txt') } 'version evidence is missing'
    Expect-Failure 'malformed-index' { param($dist) [IO.File]::WriteAllText((Join-Path $dist 'squirrel-windows\RELEASES'), 'not a release index') } 'Malformed Squirrel RELEASES line'
    Expect-Failure 'missing-index-target' { param($dist) [IO.File]::WriteAllText((Join-Path $dist 'squirrel-windows\RELEASES'), ('0' * 40) + ' missing-full.nupkg 10') } 'references a missing package'
    Expect-Failure 'index-size-mismatch' { param($dist) $path = Join-Path $dist 'squirrel-windows\exchange_auto_installer-1.2.3-full.nupkg'; $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA1).Hash.ToLowerInvariant(); [IO.File]::WriteAllText((Join-Path $dist 'squirrel-windows\RELEASES'), "$sha exchange_auto_installer-1.2.3-full.nupkg 999") } 'size mismatch'
    Expect-Failure 'index-hash-mismatch' { param($dist) $path = Join-Path $dist 'squirrel-windows\exchange_auto_installer-1.2.3-full.nupkg'; $size = (Get-Item -LiteralPath $path).Length; [IO.File]::WriteAllText((Join-Path $dist 'squirrel-windows\RELEASES'), (('0' * 40) + " exchange_auto_installer-1.2.3-full.nupkg $size")) } 'SHA-1 mismatch'
    Expect-Failure 'provenance-mismatch' { param($dist) [IO.File]::WriteAllText((Join-Path $dist 'source-commit.txt'), ('f' * 40)) } 'provenance .* does not match'
    Expect-Failure 'package-version-mismatch' { param($dist) [IO.File]::WriteAllText((Join-Path $dist 'package-version.txt'), '9.9.9') } 'does not match expected shared version'
    Expect-Failure 'corrupt-setup-pe' { param($dist) [IO.File]::WriteAllBytes((Join-Path $dist 'squirrel-windows\ExchangeAutoInstaller-1.2.3-x64-Setup.exe'), [byte[]]::new(131072)) } 'does not have an MZ header'
    Expect-Failure 'corrupt-unpacked-app-pe' { param($dist) [IO.File]::WriteAllBytes((Join-Path $dist 'win-unpacked\Exchange Auto Installer.exe'), [byte[]]::new(131072)) } 'does not have an MZ header'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Output 'PASS: 15 missing/corrupt release-asset fixtures turned red'
