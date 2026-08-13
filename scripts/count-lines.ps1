[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$JsonOutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
Push-Location $repoRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve an exact Git commit for line counting.'
    }
    & git diff --quiet --ignore-submodules --
    if ($LASTEXITCODE -ne 0) { throw 'Tracked working-tree changes are present; line counting must describe exact committed bytes.' }
    & git diff --cached --quiet --ignore-submodules --
    if ($LASTEXITCODE -ne 0) { throw 'Staged changes are present; line counting must describe exact committed bytes.' }

    $textExtensions = @(
        '.bat', '.cmd', '.cs', '.csproj', '.css', '.csv', '.editorconfig', '.gitattributes',
        '.gitignore', '.html', '.htm', '.iss', '.js', '.json', '.jsonl', '.jsx', '.md',
        '.props', '.ps1', '.psd1', '.psm1', '.resx', '.scss', '.sln', '.sql', '.targets',
        '.toml', '.ts', '.tsx', '.txt', '.xml', '.xaml', '.yaml', '.yml'
    )

    function Get-Category([string]$Path) {
        $normalized = $Path.Replace('\', '/')
        $extension = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
        if ($normalized -match '(^|/)(packages|node_modules|vendor|third_party|third-party)(/|$)') { return 'Vendored / third-party' }
        if ($normalized -match '(^|/)(bin|obj|dist|artifacts|coverage)(/|$)' -or $normalized -match '\.g\.(cs|i\.cs)$') { return 'Generated output' }
        if ($normalized -match '(^|/)(package-lock\.json|packages\.lock\.json|yarn\.lock|pnpm-lock\.yaml)$') { return 'Lockfiles' }
        if ($normalized -match '(^|/)(test|tests|__tests__)(/|$)' -or $normalized -match '(Tests?|Spec)\.(cs|js|ts|tsx)$') { return 'Tests' }
        if ($extension -in @('.xaml', '.css', '.scss', '.html', '.htm')) { return 'Styles / markup' }
        if ($normalized -match '(^|/)(\.github|scripts|packaging)(/|$)' -or $extension -in @('.bat', '.cmd', '.ps1', '.psd1', '.psm1', '.iss', '.yml', '.yaml')) { return 'Build / release tooling' }
        if ($extension -in @('.md', '.txt') -or $normalized -match '(^|/)docs(/|$)') { return 'Documentation / records' }
        if ($extension -in @('.cs', '.js', '.jsx', '.ts', '.tsx', '.sql')) { return 'Source' }
        return 'Other project text'
    }

    function Test-InProjectTotal([string]$Category) {
        return $Category -in @('Source', 'Tests', 'Styles / markup', 'Build / release tooling', 'Other project text')
    }

    function Get-ByteLineCounts([string]$LiteralPath) {
        $bytes = [IO.File]::ReadAllBytes($LiteralPath)
        if ($bytes.Length -eq 0) { return @{ total = 0; nonBlank = 0 } }
        if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
            $text = [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
        }
        elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
            $text = [Text.Encoding]::BigEndianUnicode.GetString($bytes, 2, $bytes.Length - 2)
        }
        elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            $text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
        }
        else {
            if ($bytes -contains 0) { throw "A counted text extension has NUL bytes without a recognized UTF BOM: $LiteralPath" }
            $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        }
        $lines = [regex]::Split($text, '\r\n|\n|\r')
        if ($text -match '(\r\n|\n|\r)$') {
            $lines = $lines[0..([Math]::Max(0, $lines.Count - 2))]
            if ($text -match '^(\r\n|\n|\r)$') { $lines = @('') }
        }
        $total = if ($text.Length -eq 0) { 0 } else { $lines.Count }
        $nonBlank = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
        return @{ total = $total; nonBlank = $nonBlank }
    }

    $agentPattern = '(?i)(Claude Fable 5|Claude|Codex|OpenAI|ChatGPT|github-actions\[bot\]|dependabot\[bot\])'
    $commitAgentCache = @{}
    function Test-AgentCommit([string]$Hash, [string]$Author) {
        if ($Author -match $agentPattern) { return $true }
        if ($commitAgentCache.ContainsKey($Hash)) { return [bool]$commitAgentCache[$Hash] }
        $body = (& git show -s --format=%B $Hash)
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect blamed commit $Hash." }
        $isAgent = [bool]($body -match "(?im)^Co-Authored-By:\s*.*$agentPattern")
        $commitAgentCache[$Hash] = $isAgent
        return $isAgent
    }

    $categoryOrder = @(
        'Source', 'Tests', 'Styles / markup', 'Build / release tooling', 'Other project text',
        'Documentation / records', 'Generated output', 'Vendored / third-party', 'Lockfiles'
    )
    $categories = @{}
    foreach ($name in $categoryOrder) {
        $categories[$name] = [ordered]@{ files = 0; total = 0; nonBlank = 0; included = (Test-InProjectTotal $name) }
    }
    $attribution = [ordered]@{ agent = 0; human = 0 }
    $countedFiles = 0

    $tracked = @(& git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    foreach ($relativePath in $tracked) {
        $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
        $leaf = [IO.Path]::GetFileName($relativePath).ToLowerInvariant()
        if ($extension -notin $textExtensions -and $leaf -notin @('.gitignore', '.gitattributes', '.editorconfig')) { continue }

        $fullPath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Tracked file is absent: $relativePath" }
        $category = Get-Category $relativePath
        $direct = Get-ByteLineCounts $fullPath
        $fileTotal = [int]$direct.total
        $fileNonBlank = [int]$direct.nonBlank

        if (Test-InProjectTotal $category) {
            $blame = @(& git blame --line-porcelain -- $relativePath)
            if ($LASTEXITCODE -ne 0) { throw "git blame failed for $relativePath" }
            $blameTotal = 0
            $blameNonBlank = 0
            $currentHash = $null
            $currentAuthor = $null
            foreach ($line in $blame) {
                if ($line -match '^([0-9a-f]{40})\s+\d+\s+\d+(?:\s+\d+)?$') {
                    $currentHash = $Matches[1]
                    $currentAuthor = $null
                    continue
                }
                if ($line -match '^author\s+(.*)$') {
                    $currentAuthor = $Matches[1]
                    continue
                }
                if ($line.StartsWith("`t")) {
                    if (-not $currentHash -or $null -eq $currentAuthor) { throw "Incomplete blame metadata for $relativePath" }
                    $content = $line.Substring(1)
                    $blameTotal++
                    if (-not [string]::IsNullOrWhiteSpace($content)) { $blameNonBlank++ }
                    if (Test-AgentCommit $currentHash $currentAuthor) { $attribution.agent++ } else { $attribution.human++ }
                }
            }
            if ($blameTotal -ne $fileTotal) {
                throw "Line arithmetic disagrees for ${relativePath}: file=$fileTotal blame=$blameTotal"
            }
            $fileNonBlank = $blameNonBlank
        }

        $categories[$category].files++
        $categories[$category].total += $fileTotal
        $categories[$category].nonBlank += $fileNonBlank
        $countedFiles++
    }

    $projectTotal = 0
    $projectNonBlank = 0
    $grandTotal = 0
    $grandNonBlank = 0
    foreach ($name in $categoryOrder) {
        $grandTotal += $categories[$name].total
        $grandNonBlank += $categories[$name].nonBlank
        if ($categories[$name].included) {
            $projectTotal += $categories[$name].total
            $projectNonBlank += $categories[$name].nonBlank
        }
    }
    if (($attribution.agent + $attribution.human) -ne $projectTotal) {
        throw "Attribution arithmetic disagrees: attribution=$($attribution.agent + $attribution.human) project=$projectTotal"
    }

    $rows = foreach ($name in $categoryOrder) {
        [ordered]@{
            category = $name
            files = $categories[$name].files
            totalLines = $categories[$name].total
            nonBlankLines = $categories[$name].nonBlank
            includedInProjectTotal = $categories[$name].included
        }
    }
    $result = [ordered]@{
        schemaVersion = 1
        commit = $commit
        command = 'pwsh -NoProfile -File scripts/count-lines.ps1'
        countedTextFiles = $countedFiles
        exclusions = @(
            'Binary files and unrecognized text extensions are not line-counted.',
            'Vendored and third-party trees, generated output, lockfiles, and documentation/records are excluded from the project total but remain visible in the grand total where text-countable.',
            'Attribution uses surviving lines from git blame. A line is agent-authored when its commit author or Co-Authored-By trailer identifies an automation/agent identity.'
        )
        categories = @($rows)
        totals = [ordered]@{
            project = [ordered]@{ totalLines = $projectTotal; nonBlankLines = $projectNonBlank }
            grand = [ordered]@{ totalLines = $grandTotal; nonBlankLines = $grandNonBlank }
        }
        attribution = [ordered]@{
            agentLines = $attribution.agent
            humanLines = $attribution.human
            totalLines = $attribution.agent + $attribution.human
        }
    }

    $markdown = [Text.StringBuilder]::new()
    [void]$markdown.AppendLine("# Line count at ``$commit``")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| Category | Files | Total lines | Non-blank lines | Project total |')
    [void]$markdown.AppendLine('| --- | ---: | ---: | ---: | :---: |')
    foreach ($row in $rows) {
        $included = if ($row.includedInProjectTotal) { 'Yes' } else { 'No' }
        [void]$markdown.AppendLine("| $($row.category) | $($row.files) | $($row.totalLines) | $($row.nonBlankLines) | $included |")
    }
    [void]$markdown.AppendLine("| **Project total** |  | **$projectTotal** | **$projectNonBlank** | **Yes** |")
    [void]$markdown.AppendLine("| **Grand total** |  | **$grandTotal** | **$grandNonBlank** | All countable text |")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('## Surviving-line attribution')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| Attribution | Lines |')
    [void]$markdown.AppendLine('| --- | ---: |')
    [void]$markdown.AppendLine("| Agent-authored | $($attribution.agent) |")
    [void]$markdown.AppendLine("| Human-authored | $($attribution.human) |")
    [void]$markdown.AppendLine("| **Attribution total** | **$($attribution.agent + $attribution.human)** |")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('Attribution uses surviving lines from `git blame`; it does not sum historical additions or churn. Agent authorship is detected from the commit author or an agent-identifying `Co-Authored-By` trailer.')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('Excluded from the project total: vendored/third-party trees, generated output, lockfiles, and documentation/records. Binary files and unrecognized text extensions are excluded from all line totals. The category rows keep every countable excluded area visible.')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('Reproduce with: `pwsh -NoProfile -File scripts/count-lines.ps1`.')

    $markdownText = $markdown.ToString()
    if ($OutputPath) {
        $resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
        [IO.File]::WriteAllText($resolvedOutput, $markdownText, [Text.UTF8Encoding]::new($false))
    }
    if ($JsonOutputPath) {
        $resolvedJson = [IO.Path]::GetFullPath((Join-Path $repoRoot $JsonOutputPath))
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedJson) -Force | Out-Null
        [IO.File]::WriteAllText($resolvedJson, ($result | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    }
    Write-Output $markdownText
}
finally {
    Pop-Location
}
