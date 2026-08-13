[CmdletBinding()]
param(
    [string]$WorkflowPath = '.github/workflows/windows-release.yml',
    [string]$InventoryPath = 'scripts/release-dependencies.json',
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Assert-ReleaseContract([string]$Workflow, [object]$Inventory, [string]$PublishScript, [string]$BuildScript, [string]$InstallerScript) {
    $requiredWorkflowPatterns = [ordered]@{
        'push branch trigger' = '(?m)^\s{2}push:\s*$'
        'workflow dispatch trigger' = '(?m)^\s{2}workflow_dispatch:\s*$'
        'actions read permission' = '(?m)^\s{2}actions:\s+read\s*$'
        'contents write permission' = '(?m)^\s{2}contents:\s+write\s*$'
        'Windows 2025 runner' = '(?m)^\s{4}runs-on:\s+windows-2025\s*$'
        'full-history checkout' = '(?m)^\s{10}fetch-depth:\s+0\s*$'
        'root runnable build' = '(?m)^\s{8}run:\s+build\.bat /s\s*$'
        'root installer build' = '(?m)^\s{8}run:\s+build-installer\.bat /s\s*$'
        'line-count evidence' = 'scripts/count-lines\.ps1'
        'dim-sum resolver' = 'scripts/resolve-dim-sum\.ps1'
        'allowlisted assembly' = 'scripts/assemble-release\.ps1'
        'single release publisher' = 'scripts/publish-release\.ps1'
        'always evidence collection' = '(?m)^\s{8}if:\s+\$\{\{ always\(\) \}\}\s*$'
        'nonmasking evidence collection' = '(?m)^\s{8}continue-on-error:\s+true\s*$'
        'warn for missing artifact files' = '(?m)^\s{10}if-no-files-found:\s+warn\s*$'
        'bounded artifact retention' = '(?m)^\s{10}retention-days:\s+\d+\s*$'
        'disabled certificate discovery' = '(?m)^\s{6}CSC_IDENTITY_AUTO_DISCOVERY:\s+''false''\s*$'
        'release token fallback chain' = 'secrets\.RELEASE_TOKEN \|\| secrets\.ORG_TOKEN \|\| secrets\.GITHUB_TOKEN'
    }
    foreach ($entry in $requiredWorkflowPatterns.GetEnumerator()) {
        if ($Workflow -notmatch $entry.Value) { throw "Release workflow is missing $($entry.Key)." }
    }
    if ($Workflow -match '(?m)^\s{2}(pull_request|pull_request_target):') { throw 'Release workflow must not run from pull requests.' }
    if ($Workflow -match '(?im)^\s*(?:name|run):.*\b(test|tests|lint|type[- ]?check|static[- ]?analysis|coverage|screenshot|accessibility)\b') {
        throw 'Release workflow contains a prohibited test, lint, analysis, accessibility, coverage, or screenshot command/job.'
    }
    if ($Workflow -match '(?m)^\s+needs:') { throw 'Release publication must not depend on another job chain.' }
    if ($Workflow -match '(?m)^\s+cancel-in-progress:\s+true\s*$') { throw 'Release work must not cancel an older publication run.' }
    if ($Workflow -match '(?m)^\s*concurrency:\s*$') { throw 'A per-ref release concurrency group can cancel pending push publications; this release workflow must not use one.' }

    $uses = [regex]::Matches($Workflow, '(?m)^\s*uses:\s+([^@\s]+)@([^\s]+)\s*$')
    if ($uses.Count -lt 3) { throw 'Release workflow must use pinned checkout, setup-node, and upload-artifact actions.' }
    foreach ($match in $uses) {
        if ($match.Groups[2].Value -notmatch '^[0-9a-f]{40}$') { throw "Action is not pinned to a full commit SHA: $($match.Value.Trim())" }
    }

    $jobsText = $Workflow.Substring($Workflow.IndexOf("`njobs:", [StringComparison]::Ordinal) + 1)
    $jobIds = @([regex]::Matches($jobsText, '(?m)^  ([A-Za-z0-9_-]+):\s*$') | ForEach-Object { $_.Groups[1].Value })
    if ($jobIds.Count -ne 1 -or $jobIds[0] -cne 'windows_release') { throw "Expected exactly one windows_release job; found: $($jobIds -join ', ')" }
    $inventoryJobIds = @($Inventory.jobs | ForEach-Object { [string]$_.id })
    if ($inventoryJobIds.Count -ne $jobIds.Count -or (Compare-Object $jobIds $inventoryJobIds)) { throw 'Workflow jobs and the hand-written dependency inventory disagree.' }
    if ([string]$Inventory.workflow -cne '.github/workflows/windows-release.yml') { throw 'Dependency inventory points at the wrong workflow.' }
    foreach ($job in $Inventory.jobs) {
        if ([string]$job.runner -cne 'windows-2025') { throw "Inventory runner mismatch for $($job.id)." }
        if ([string]::IsNullOrWhiteSpace([string]$job.bootstrapProof) -or [string]::IsNullOrWhiteSpace([string]$job.firstRealWork)) { throw "Inventory bootstrap proof is incomplete for $($job.id)." }
        if (@($job.dependencies).Count -lt 1 -or @($job.safeOutputs).Count -lt 1) { throw "Inventory dependency or safe-output list is empty for $($job.id)." }
        foreach ($dependency in $job.dependencies) {
            foreach ($field in @('name', 'constraint', 'source', 'bootstrap')) {
                if ([string]::IsNullOrWhiteSpace([string]$dependency.$field)) { throw "Dependency field '$field' is missing for $($job.id)." }
            }
        }
    }

    foreach ($needle in @('npm.cmd" ci', 'npm.cmd" run build', 'Exchange Auto Installer.exe', 'source-commit.txt')) {
        if (-not $BuildScript.Contains($needle, [StringComparison]::OrdinalIgnoreCase)) { throw "build.bat is missing Exchange orchestration evidence: $needle" }
    }
    foreach ($needle in @('package-exchange.ps1', 'verify-exchange-package.ps1', 'Exchange Squirrel.Windows output')) {
        if (-not $InstallerScript.Contains($needle, [StringComparison]::OrdinalIgnoreCase)) { throw "build-installer.bat is missing Exchange packaging evidence: $needle" }
    }
    if ($InstallerScript -match '(?im)^\s*git\s+restore\b') { throw 'build-installer.bat must not discard local edits with git restore.' }
    foreach ($needle in @('gh release create', 'gh release edit', 'gh release download', 'published_at', 'Workflow duration')) {
        if (-not $PublishScript.Contains($needle, [StringComparison]::OrdinalIgnoreCase)) { throw "Release publisher is missing required proof: $needle" }
    }
}

$workflowFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $WorkflowPath))
$inventoryFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $InventoryPath))
$workflow = Get-Content -LiteralPath $workflowFullPath -Raw
$inventory = Get-Content -LiteralPath $inventoryFullPath -Raw | ConvertFrom-Json
$publish = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'publish-release.ps1') -Raw
$build = Get-Content -LiteralPath (Join-Path $repoRoot 'build.bat') -Raw
$installer = Get-Content -LiteralPath (Join-Path $repoRoot 'build-installer.bat') -Raw

Assert-ReleaseContract $workflow $inventory $publish $build $installer
Write-Output 'PASS: release workflow and dependency inventory contract'

if ($SelfTest) {
    $mutations = [ordered]@{
        'missing workflow_dispatch' = @{ workflow = $workflow -replace '(?m)^\s{2}workflow_dispatch:\s*\r?\n', ''; publish = $publish; build = $build; installer = $installer }
        'wrong runner' = @{ workflow = $workflow -replace 'runs-on: windows-2025', 'runs-on: ubuntu-latest'; publish = $publish; build = $build; installer = $installer }
        'missing root installer build' = @{ workflow = $workflow -replace '(?m)^\s{8}run:\s+build-installer\.bat /s\s*\r?\n', ''; publish = $publish; build = $build; installer = $installer }
        'prohibited test command' = @{ workflow = $workflow + "`n      - name: Unit tests`n        run: npm test`n"; publish = $publish; build = $build; installer = $installer }
        'missing always evidence boundary' = @{ workflow = $workflow -replace '\$\{\{ always\(\) \}\}', '${{ success() }}'; publish = $publish; build = $build; installer = $installer }
        'mutable action tag' = @{ workflow = $workflow -replace 'actions/upload-artifact@[0-9a-f]{40}', 'actions/upload-artifact@v4'; publish = $publish; build = $build; installer = $installer }
        'missing release download proof' = @{ workflow = $workflow; publish = $publish -replace 'gh release download', 'gh release inspect'; build = $build; installer = $installer }
        'missing Exchange package verifier' = @{ workflow = $workflow; publish = $publish; build = $build; installer = $installer -replace 'verify-exchange-package\.ps1', 'missing-verifier.ps1' }
    }
    $red = 0
    foreach ($entry in $mutations.GetEnumerator()) {
        try {
            Assert-ReleaseContract $entry.Value.workflow $inventory $entry.Value.publish $entry.Value.build $entry.Value.installer
        }
        catch {
            $red++
            Write-Output "EXPECTED RED: $($entry.Key) -> $($_.Exception.Message)"
        }
    }
    if ($red -ne $mutations.Count) { throw "Negative release-contract regression failed: $red/$($mutations.Count) mutations turned red." }
    Write-Output "PASS: negative release-contract regression ($red/$($mutations.Count) deliberate mutations turned red)"
}
