[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Repository,
    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$catalogRepository = 'Ding-Ding-Projects/dim-sum-photos'
$catalogPath = 'catalog/index.json'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null

$result = [ordered]@{
    schemaVersion = 1
    available = $false
    reason = $null
    codeName = $null
    name = $null
    catalogRevision = $null
    catalogUrl = 'https://raw.githubusercontent.com/Ding-Ding-Projects/dim-sum-photos/main/catalog/index.json'
    photoAsset = $null
    photoUrl = $null
}

try {
    $contentMetadata = (& gh api "repos/$catalogRepository/contents/$catalogPath" | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or -not $contentMetadata.sha) { throw 'The public catalog metadata could not be resolved.' }
    $result.catalogRevision = [string]$contentMetadata.sha

    $blob = (& gh api "repos/$catalogRepository/git/blobs/$($contentMetadata.sha)" | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or -not $blob.content) { throw 'The public catalog blob could not be resolved.' }
    $catalogText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(([string]$blob.content -replace '\s', '')))
    $catalog = $catalogText | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or -not $catalog.dishes) { throw 'The public catalog schema is unavailable or unsupported.' }

    $releasePages = & gh api "repos/$Repository/releases?per_page=100" --paginate --slurp
    if ($LASTEXITCODE -ne 0) { throw 'Existing project releases could not be inspected for prior code-name use.' }
    $usedText = (($releasePages | ConvertFrom-Json) | ForEach-Object { $_ } | ForEach-Object { $_.body }) -join "`n"

    $catalogTags = @(& gh api "repos/$catalogRepository/releases?per_page=100" --paginate --jq '.[] | select(.draft == false and .prerelease == false and (.tag_name | startswith("catalog-v1"))) | .tag_name')
    if ($LASTEXITCODE -ne 0 -or $catalogTags.Count -eq 0) { throw 'No published catalog-v1 release was found.' }

    $selected = $null
    foreach ($dish in $catalog.dishes) {
        $codeName = "$($dish.name.en) · $($dish.name.zhHant)"
        if ($usedText.Contains($codeName, [StringComparison]::Ordinal)) { continue }
        $assetName = [IO.Path]::GetFileName([string]$dish.image.path)
        if ([string]::IsNullOrWhiteSpace($assetName)) { continue }
        foreach ($tag in $catalogTags) {
            $assetUrl = "https://github.com/$catalogRepository/releases/download/$tag/$assetName"
            try {
                $response = Invoke-WebRequest -UseBasicParsing -Method Head -Uri $assetUrl -MaximumRedirection 5 -TimeoutSec 20
                if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                    $selected = [ordered]@{
                        codeName = $codeName
                        name = [ordered]@{ en = [string]$dish.name.en; zhHant = [string]$dish.name.zhHant }
                        photoAsset = $assetName
                        photoUrl = $assetUrl
                    }
                    break
                }
            }
            catch {
                # A missing asset in one catalog volume is expected; continue to the next published volume.
            }
        }
        if ($selected) { break }
    }

    if ($selected) {
        $result.available = $true
        $result.codeName = $selected.codeName
        $result.name = $selected.name
        $result.photoAsset = $selected.photoAsset
        $result.photoUrl = $selected.photoUrl
    }
    else {
        $result.reason = 'No unused dish with a verified published catalog-v1 photo asset was resolved. The release must ship without a code name rather than reuse or invent one.'
    }
}
catch {
    $result.reason = "Dim-sum catalog lookup was unavailable: $($_.Exception.Message) The release must ship without a code name; catalog decoration does not block publication."
}

[IO.File]::WriteAllText($resolvedOutput, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
if ($result.available) {
    Write-Output "Resolved unused published dim-sum code name: $($result.codeName)"
    Write-Output "Public photo: $($result.photoUrl)"
}
else {
    Write-Warning $result.reason
}
