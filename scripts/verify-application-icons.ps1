[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Resolve-OwnedPath([string]$OwnerFile, [string]$RelativePath, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { throw "$Label does not declare an icon path." }
    if ([IO.Path]::IsPathRooted($RelativePath)) { throw "$Label icon path must be repository-relative, not absolute: $RelativePath" }
    $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $OwnerFile) $RelativePath))
    if (-not $resolved.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label icon escapes the repository: $resolved"
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "$Label icon is missing: $resolved" }
    $relative = [IO.Path]::GetRelativePath($repoRoot, $resolved).Replace('\', '/')
    & git -C $repoRoot ls-files --error-unmatch -- $relative *> $null
    if ($LASTEXITCODE -ne 0) { throw "$Label icon is not tracked by Git: $relative" }
    return $resolved
}

function Assert-MultiResolutionIco([string]$Path, [string]$Label) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 6) { throw "$Label is too short to be an ICO file: $Path" }
    $reserved = [BitConverter]::ToUInt16($bytes, 0)
    $type = [BitConverter]::ToUInt16($bytes, 2)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 1) { throw "$Label does not have a valid ICO directory header: $Path" }
    if ($bytes.Length -lt (6 + (16 * $count))) { throw "$Label has a truncated ICO directory: $Path" }
    $sizes = [Collections.Generic.HashSet[int]]::new()
    for ($index = 0; $index -lt $count; $index++) {
        $offset = 6 + (16 * $index)
        $width = if ($bytes[$offset] -eq 0) { 256 } else { [int]$bytes[$offset] }
        $height = if ($bytes[$offset + 1] -eq 0) { 256 } else { [int]$bytes[$offset + 1] }
        if ($width -ne $height) { throw "$Label contains a non-square ICO entry (${width}x${height}): $Path" }
        $imageBytes = [BitConverter]::ToUInt32($bytes, $offset + 8)
        $imageOffset = [BitConverter]::ToUInt32($bytes, $offset + 12)
        if ($imageBytes -eq 0 -or ([uint64]$imageOffset + [uint64]$imageBytes) -gt [uint64]$bytes.Length) {
            throw "$Label contains an invalid ICO image entry at index ${index}: $Path"
        }
        [void]$sizes.Add($width)
    }
    $required = @(16, 32, 48, 256)
    $missing = @($required | Where-Object { -not $sizes.Contains($_) })
    if ($missing.Count -gt 0) { throw "$Label is missing required ICO sizes $($missing -join ', '): $Path" }
    Write-Output "$Label ICO sizes: $(@($sizes | Sort-Object) -join ', ') at $Path"
}

$wpfProject = Join-Path $repoRoot 'Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj'
[xml]$wpfXml = Get-Content -LiteralPath $wpfProject -Raw
$wpfIconValue = @($wpfXml.SelectNodes('//*[local-name()="ApplicationIcon"]') | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ }) | Select-Object -First 1
$wpfIcon = Resolve-OwnedPath $wpfProject $wpfIconValue 'WPF ApplicationIcon'
Assert-MultiResolutionIco $wpfIcon 'WPF ApplicationIcon'

$innoFile = Join-Path $repoRoot 'packaging\WindowsServerTools.iss'
$innoText = Get-Content -LiteralPath $innoFile -Raw
$innoMatch = [regex]::Match($innoText, '(?im)^SetupIconFile\s*=\s*"?([^"\r\n]+)"?\s*$')
if (-not $innoMatch.Success) { throw 'packaging/WindowsServerTools.iss does not declare SetupIconFile.' }
$innoIcon = Resolve-OwnedPath $innoFile $innoMatch.Groups[1].Value.Trim() 'Inno SetupIconFile'
Assert-MultiResolutionIco $innoIcon 'Inno SetupIconFile'

$exchangePackageFile = Join-Path $repoRoot 'Windows-Server-Tools\Exchange-Auto-Installer\package.json'
$exchangePackage = Get-Content -LiteralPath $exchangePackageFile -Raw | ConvertFrom-Json
$exchangeIconProperty = $exchangePackage.build.win.PSObject.Properties['icon']
$exchangeIconValue = if ($exchangeIconProperty) { [string]$exchangeIconProperty.Value } else { '' }
$exchangeIcon = Resolve-OwnedPath $exchangePackageFile $exchangeIconValue 'Exchange win.icon'
Assert-MultiResolutionIco $exchangeIcon 'Exchange win.icon'

Write-Output 'PASS: both applications and both installer families declare tracked multi-resolution icons.'
