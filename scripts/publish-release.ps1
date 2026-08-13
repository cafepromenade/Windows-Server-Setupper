[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Repository,
    [string]$StagingDirectory = 'release-staging',
    [string]$JobStartFile = 'release-evidence/job-start.txt'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $StagingDirectory))
$startPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $JobStartFile))
if (-not (Test-Path -LiteralPath $stagingRoot -PathType Container)) { throw "Release staging is missing: $stagingRoot" }
if (-not (Test-Path -LiteralPath $startPath -PathType Leaf)) { throw "Job start evidence is missing: $startPath" }

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Could not resolve the release commit.' }
if ($env:GITHUB_SHA -cne $commit) { throw "GITHUB_SHA $env:GITHUB_SHA does not match checked-out commit $commit." }

$start = [DateTimeOffset]::Parse((Get-Content -LiteralPath $startPath -Raw).Trim(), [Globalization.CultureInfo]::InvariantCulture)
$short = $commit.Substring(0, 8)
$tag = "windows-$($env:GITHUB_RUN_NUMBER).$($env:GITHUB_RUN_ATTEMPT)-$short"
$releaseName = "Windows build $($env:GITHUB_RUN_NUMBER).$($env:GITHUB_RUN_ATTEMPT)"

& gh api "repos/$Repository/releases/tags/$tag" --silent 2>$null
if ($LASTEXITCODE -eq 0) { throw "Release tag already exists: $tag" }
& gh api "repos/$Repository/git/ref/tags/$tag" --silent 2>$null
if ($LASTEXITCODE -eq 0) { throw "Git tag already exists: $tag" }

$dimSum = Get-Content -LiteralPath (Join-Path $stagingRoot 'dim-sum.json') -Raw | ConvertFrom-Json
$lineCount = Get-Content -LiteralPath (Join-Path $stagingRoot 'line-count.md') -Raw
$manifest = Get-Content -LiteralPath (Join-Path $stagingRoot 'artifact-manifest.json') -Raw | ConvertFrom-Json
$packageVersion = [string]$manifest.packageVersion
if ($packageVersion -notmatch '^\d+\.\d+\.\d+$' -or $packageVersion -cne [string]$env:WST_RELEASE_VERSION) {
    throw "Release manifest package version '$packageVersion' does not match WST_RELEASE_VERSION '$env:WST_RELEASE_VERSION'."
}
if ($dimSum.available) { $releaseName += " · $($dimSum.codeName)" }

function New-ReleaseNotes([string]$CompletedAt, [string]$Duration) {
    $builder = [Text.StringBuilder]::new()
    [void]$builder.AppendLine("# $releaseName")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("Windows-only unsigned release version ``$packageVersion`` for commit ``$commit``.")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('> [!WARNING]')
    [void]$builder.AppendLine('> The installers are intentionally unsigned and may trigger Windows unknown-publisher or SmartScreen warnings. No code-signing certificate or signing service was used.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## Workflow timing')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("- Workflow started: ``$($start.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ'))``")
    [void]$builder.AppendLine("- Workflow completed: ``$CompletedAt``")
    [void]$builder.AppendLine("- Workflow duration: ``$Duration``")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('The interval begins at the GitHub Actions job `started_at` value and ends at the server-reported publication time for this non-draft release.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## Build and verification boundary')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('- `build.bat /s` built the runnable WPF and Electron applications through their supported paths.')
    [void]$builder.AppendLine('- `build-installer.bat /s` produced the Inno Setup installer and the complete Squirrel.Windows setup/update set.')
    [void]$builder.AppendLine('- Both setup executables were verified to contain no PE certificate table.')
    [void]$builder.AppendLine('- GitHub Actions ran no tests, lint, type checks, static analysis, accessibility checks, or screenshot checks. This intentionally ungated delivery can publish a commit whose local tests would fail; the first report may come from someone running an installer.')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## Install and update assets')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('| Role | Asset | Bytes | SHA-256 |')
    [void]$builder.AppendLine('| --- | --- | ---: | --- |')
    foreach ($artifact in $manifest.artifacts) {
        [void]$builder.AppendLine("| $($artifact.role) | ``$($artifact.name)`` | $($artifact.bytes) | ``$($artifact.sha256)`` |")
    }
    [void]$builder.AppendLine()
    if ($dimSum.available) {
        [void]$builder.AppendLine('## Dim-sum code name')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("**$($dimSum.codeName)** — [public catalog photo]($($dimSum.photoUrl))")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("Catalog revision: ``$($dimSum.catalogRevision)``. The photo remains in the public catalog release; this consumer release does not copy or attach it.")
    }
    else {
        [void]$builder.AppendLine('## Dim-sum code name')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("No unused published catalog dish was resolved: $($dimSum.reason)")
    }
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('## Line count')
    [void]$builder.AppendLine()
    [void]$builder.AppendLine($lineCount.Trim())
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('The attached `artifact-manifest.json`, `SHA256SUMS.txt`, `line-count.json`, `release-dependencies.json`, and `run-context.json` make the release evidence reproducible.')
    return $builder.ToString()
}

$initialNotesPath = Join-Path $stagingRoot 'release-notes-initial.md'
$initialNotes = New-ReleaseNotes -CompletedAt 'Pending server publication timestamp' -Duration 'Pending server publication timestamp'
[IO.File]::WriteAllText($initialNotesPath, $initialNotes, [Text.UTF8Encoding]::new($false))

$assets = @(Get-ChildItem -LiteralPath $stagingRoot -File | Where-Object { $_.Name -ne 'release-notes-initial.md' } | Sort-Object Name | ForEach-Object { $_.FullName })
if ($assets.Count -eq 0) { throw 'No release assets were staged.' }

& gh release create $tag --repo $Repository --target $commit --title $releaseName --notes-file $initialNotesPath --draft=false --prerelease=false @assets
if ($LASTEXITCODE -ne 0) { throw "gh release create failed for $tag." }

$published = & gh api "repos/$Repository/releases/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $published.draft -or $published.prerelease) { throw "Published release $tag is absent, draft, or prerelease." }
if ([string]$published.target_commitish -cne $commit) { throw "Release target $($published.target_commitish) does not match $commit." }
$completed = [DateTimeOffset]::Parse([string]$published.published_at, [Globalization.CultureInfo]::InvariantCulture)
$elapsed = $completed - $start
if ($elapsed.TotalSeconds -lt 0) { throw 'Server publication time precedes the recorded job start.' }
$duration = '{0:00}:{1:00}:{2:00}' -f [Math]::Floor($elapsed.TotalHours), $elapsed.Minutes, $elapsed.Seconds
$completedText = $completed.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')

$finalNotesPath = Join-Path $stagingRoot 'release-notes.md'
[IO.File]::WriteAllText($finalNotesPath, (New-ReleaseNotes -CompletedAt $completedText -Duration $duration), [Text.UTF8Encoding]::new($false))
& gh release edit $tag --repo $Repository --notes-file $finalNotesPath
if ($LASTEXITCODE -ne 0) { throw "Could not finalize release notes for $tag." }

$verifyRoot = Join-Path $repoRoot '.release-verify'
if (Test-Path -LiteralPath $verifyRoot) { Remove-Item -LiteralPath $verifyRoot -Recurse -Force }
New-Item -ItemType Directory -Path $verifyRoot -Force | Out-Null
try {
    & gh release download $tag --repo $Repository --dir $verifyRoot
    if ($LASTEXITCODE -ne 0) { throw "Release assets for $tag were not downloadable." }
    foreach ($asset in $assets) {
        $name = [IO.Path]::GetFileName($asset)
        $downloaded = Join-Path $verifyRoot $name
        if (-not (Test-Path -LiteralPath $downloaded -PathType Leaf)) { throw "Downloaded release is missing $name." }
        $expectedHash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
        $actualHash = (Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash
        if ($expectedHash -cne $actualHash) { throw "Downloaded release asset hash mismatch: $name" }
    }
}
finally {
    if (Test-Path -LiteralPath $verifyRoot) { Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

$verified = & gh api "repos/$Repository/releases/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $verified.draft -or $verified.prerelease -or $verified.assets.Count -ne $assets.Count) {
    throw "Final release verification failed for $tag."
}
if ([string]$verified.tag_name -cne $tag -or [string]$verified.name -cne $releaseName -or [string]$verified.target_commitish -cne $commit) {
    throw "Final release identity does not match tag $tag, title '$releaseName', and commit $commit."
}
$expectedBody = (Get-Content -LiteralPath $finalNotesPath -Raw) -replace "`r`n", "`n"
$actualBody = ([string]$verified.body) -replace "`r`n", "`n"
if ($actualBody -cne $expectedBody) { throw "Final release notes do not match the verified timing body for $tag." }
foreach ($asset in $assets) {
    $local = Get-Item -LiteralPath $asset
    $matches = @($verified.assets | Where-Object { [string]$_.name -ceq $local.Name })
    if ($matches.Count -ne 1) { throw "Final release asset identity is missing or duplicated: $($local.Name)" }
    if ([int64]$matches[0].size -ne $local.Length -or [string]$matches[0].state -cne 'uploaded') {
        throw "Final release asset size/state mismatch: $($local.Name)"
    }
    $downloadUri = [Uri]([string]$matches[0].browser_download_url)
    if ($downloadUri.Scheme -cne 'https' -or $downloadUri.Host -cne 'github.com') {
        throw "Final release asset has an unexpected download URL: $($local.Name)"
    }
}
Write-Output "Published and verified one non-draft release: $($verified.html_url)"
Write-Output "Tag: $tag"
Write-Output "Target: $commit"
Write-Output "Timing: $($start.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')) to $completedText ($duration)"
