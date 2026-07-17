#requires -version 5.1
<#
.SYNOPSIS
  ArchDiagram launcher: point at a local project folder or GitLab URL and
  generate a fully-offline static HTML documentation site (overview, structure,
  dependency graphs, types, call graphs, per-file pages).

.DESCRIPTION
  Reads archdiagram.config.json (same folder). Double-click ArchDiagram.cmd,
  or run directly:

    powershell -File Launch-ArchDiagram.ps1                 # use the config as-is
    powershell -File Launch-ArchDiagram.ps1 -ProjectPath C:\repos\foo
    powershell -File Launch-ArchDiagram.ps1 -GitLabUrl https://gitlab.example.com/team/app.git -GitRef main
#>
[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$ProjectPath,
    [string]$GitLabUrl,
    [string]$GitRef,
    [string]$OutputDir,
    [int]$MaxNodes,
    [switch]$Landscape
)

$ErrorActionPreference = 'Stop'
$LauncherDir = $PSScriptRoot
$ToolProject = Join-Path $LauncherDir 'src\ArchDiagram'

$env:MSBUILDDISABLENODEREUSE = '1' #Avoids running out of memory on some machines when scanning large projects with many files.

function Write-Head($text) { Write-Host ""; Write-Host "== $text ==" -ForegroundColor Cyan }
function Write-Info($text) { Write-Host "  $text" -ForegroundColor Gray }
function Fail($text) { Write-Host ""; Write-Host "ERROR: $text" -ForegroundColor Red; Read-Host "Press Enter to exit"; exit 1 }

function Test-Tool($name) {
    return $null -ne (Get-Command $name -ErrorAction SilentlyContinue)
}

function Resolve-PathMaybeRelative($value, $base) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    if ([System.IO.Path]::IsPathRooted($value)) { return $value }
    return (Join-Path $base $value)
}

# ---- Load config -----------------------------------------------------------

if (-not $ConfigPath) { $ConfigPath = Join-Path $LauncherDir 'archdiagram.config.json' }
if (-not (Test-Path $ConfigPath)) { Fail "Config file not found: $ConfigPath" }

try {
    $cfg = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
} catch {
    Fail "Could not parse config JSON ($ConfigPath): $($_.Exception.Message)"
}

if (-not $ProjectPath) { $ProjectPath = $cfg.ProjectPath }
if (-not $GitLabUrl)   { $GitLabUrl   = $cfg.GitLabUrl }
if (-not $GitRef)      { $GitRef      = $cfg.GitRef }
if (-not $OutputDir)   { $OutputDir   = $cfg.OutputDir }
if (-not $MaxNodes -and $null -ne $cfg.MaxNodes) { $MaxNodes = [int]$cfg.MaxNodes }

$OpenViewer = $true
if ($null -ne $cfg.OpenViewer) { $OpenViewer = [bool]$cfg.OpenViewer }

$Complexity = $true
if ($null -ne $cfg.Complexity) { $Complexity = [bool]$cfg.Complexity }

$Snippets = $true
if ($null -ne $cfg.Snippets) { $Snippets = [bool]$cfg.Snippets }

$Wiki = $true
if ($null -ne $cfg.Wiki) { $Wiki = [bool]$cfg.Wiki }

# Optional list of directory names to skip during scanning (e.g. obj, bin, node_modules).
$Exclude = @()
if ($cfg.Exclude) { $Exclude = @($cfg.Exclude) }

# Optional source linking so generated pages/nodes link back to source.
$SourceLinkType = ''
if ($cfg.SourceLinkType) { $SourceLinkType = [string]$cfg.SourceLinkType }
$SourceLinkBase = ''
if ($cfg.SourceLinkBase) { $SourceLinkBase = [string]$cfg.SourceLinkBase }
$SourceLinkRef = ''
if ($cfg.SourceLinkRef) { $SourceLinkRef = [string]$cfg.SourceLinkRef }

# Optional path to an authored descriptions sidecar (archdiagram.descriptions.json). Leave
# blank to let each scanned source auto-discover its own sidecar at its root.
$DescriptionsFile = ''
if ($cfg.DescriptionsFile) { $DescriptionsFile = [string]$cfg.DescriptionsFile }

# Builds the shared scan-time arguments (exclude dirs + source linking + descriptions) appended
# to both the group scan path and the single-project run.
function Add-CommonScanArgs($argList, $linkType = $null, $linkBase = $null, $linkRef = $null) {
    # $linkType/$linkBase/$linkRef let a caller (group mode) override the global
    # SourceLink* config per-project; $null means "use the global config value".
    if ($null -eq $linkType) { $linkType = $SourceLinkType }
    if ($null -eq $linkBase) { $linkBase = $SourceLinkBase }
    if ($null -eq $linkRef)  { $linkRef  = $SourceLinkRef }
    foreach ($ex in $Exclude) { if ($ex) { $argList += @('--exclude', [string]$ex) } }
    if ($linkType) { $argList += @('--source-link-type', $linkType) }
    if ($linkBase) { $argList += @('--source-link-base', $linkBase) }
    if ($linkRef)  { $argList += @('--source-link-ref', $linkRef) }
    if ($DescriptionsFile) { $argList += @('--descriptions', $DescriptionsFile) }
    return $argList
}

if (-not $Landscape -and $null -ne $cfg.Landscape) { $Landscape = [bool]$cfg.Landscape }

$HasGroups = ($cfg.Groups -and @($cfg.Groups).Count -gt 0)

# ---- Landscape mode --------------------------------------------------------
# Cross-references every generated site-*/model.json in the launcher folder into
# a parent viewer at site-landscape. No source is scanned. Group mode (below)
# takes precedence over this legacy single-landscape switch.

if ($Landscape -and -not $HasGroups) {
    if (-not (Test-Tool 'dotnet')) { Fail "'dotnet' is not on PATH. Install the .NET SDK first." }
    Write-Head "ArchDiagram - Landscape"
    $lOut = Join-Path $LauncherDir 'site-landscape'
    Write-Info "Cross-referencing sites under: $LauncherDir"
    Write-Info "Output: $lOut"
    $toolArgs = @('run', '--project', $ToolProject, '--', '--landscape', $LauncherDir, '--out', $lOut)
    if (-not $OpenViewer) { $toolArgs += '--no-open' }
    & dotnet @toolArgs
    $code = $LASTEXITCODE
    if ($code -ne 0) { Fail "archdiagram exited with code $code. See output above." }
    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    exit 0
}

$WorkDir = Resolve-PathMaybeRelative $cfg.WorkDir $LauncherDir
if (-not $WorkDir) { $WorkDir = Join-Path $LauncherDir 'work' }

function ConvertTo-HttpsRepoUrl($sshUrl) {
    # git@host:group/sub/repo.git -> https://host/group/sub/repo (used to auto-derive
    # per-project SourceLinkBase in group mode, since each project's own GitLab path
    # differs and a single global SourceLinkBase can't cover more than one repo).
    if ($sshUrl -notmatch '^[\w.\-]+@([^:]+):(.+?)(\.git)?$') { return '' }
    return "https://$($Matches[1])/$($Matches[2])"
}

function Get-Slug($name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return 'project' }
    $slug = ($name -replace '[^A-Za-z0-9\-_.]', '-').Trim('-', '.')
    if ([string]::IsNullOrWhiteSpace($slug)) { return 'project' }
    return $slug
}

# ---- Prerequisite checks ---------------------------------------------------

if (-not (Test-Tool 'dotnet')) { Fail "'dotnet' is not on PATH. Install the .NET SDK first." }

# ---- Resolve source ---------------------------------------------------------

function Invoke-CloneOrUpdate($url, $ref, $destParent) {
    if (-not (Test-Tool 'git')) { Fail "'git' is not on PATH but a GitLabUrl is configured." }

    $leaf = ($url -split '[\\/]')[-1]
    if ($leaf.EndsWith('.git')) { $leaf = $leaf.Substring(0, $leaf.Length - 4) }
    if ([string]::IsNullOrWhiteSpace($leaf)) { $leaf = 'repo' }
    $dest = Join-Path $destParent $leaf

    if (Test-Path (Join-Path $dest '.git')) {
        Write-Info "Updating existing clone: $dest"
        & git -C $dest fetch --prune | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "git fetch failed." }
        if ($ref) {
            & git -C $dest checkout $ref | Out-Null
            if ($LASTEXITCODE -ne 0) { Fail "git checkout '$ref' failed." }
        }
        & git -C $dest pull --ff-only | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "git pull failed." }
    } else {
        New-Item -ItemType Directory -Force -Path $destParent | Out-Null
        Write-Info "Cloning $url -> $dest"
        if ($ref) { & git clone --branch $ref $url $dest | Out-Null } else { & git clone $url $dest | Out-Null }
        if ($LASTEXITCODE -ne 0) { Fail "git clone failed." }
    }
    return $dest
}

# ---- Group mode ------------------------------------------------------------
# When Groups is a non-empty array, scan every project in each group into its
# own site-<project> folder, build one landscape per group, then optionally an
# overall landscape across all groups. Reuses the single-project scan engine.

function Invoke-ScanProject($scanTarget, $outputDir, $linkType = $null, $linkBase = $null, $linkRef = $null) {
    Write-Info "Scan: $scanTarget"
    Write-Info "Output: $outputDir"
    $toolArgs = @('run', '--project', $ToolProject, '--', $scanTarget, '--out', $outputDir)
    if ($MaxNodes -gt 0) { $toolArgs += @('--max-nodes', $MaxNodes) }
    $toolArgs += '--no-open'        # never open individual sites during a group run
    if (-not $Complexity) { $toolArgs += '--no-complexity' }
    if (-not $Snippets)   { $toolArgs += '--no-snippets' }
    if (-not $Wiki)       { $toolArgs += '--no-wiki' }
    $toolArgs = Add-CommonScanArgs $toolArgs $linkType $linkBase $linkRef
    & dotnet @toolArgs
    if ($LASTEXITCODE -ne 0) { Fail "archdiagram exited with code $LASTEXITCODE while scanning $scanTarget." }
}

function Resolve-ProjectEntry($entry) {
    # A group project is either a local path string or an object {GitLabUrl, GitRef}.
    if ($entry -is [string]) {
        if (-not (Test-Path -LiteralPath $entry)) { Fail "Group project path does not exist: $entry" }
        return (Resolve-Path -LiteralPath $entry).Path
    }
    if ([string]::IsNullOrWhiteSpace($entry.GitLabUrl)) { Fail "Group project object needs a GitLabUrl (or use a local path string)." }
    return (Invoke-CloneOrUpdate $entry.GitLabUrl $entry.GitRef $WorkDir)
}

if ($HasGroups) {
    Write-Head "ArchDiagram - Groups"
    foreach ($group in @($cfg.Groups)) {
        if ([string]::IsNullOrWhiteSpace($group.Name)) { Fail "Every group needs a non-empty Name." }
        if (-not $group.Projects -or @($group.Projects).Count -eq 0) { Fail "Group '$($group.Name)' has no Projects." }
        $groupSlug = Get-Slug $group.Name
        Write-Head "Group: $($group.Name)"
        $siteFolders = @()
        foreach ($entry in @($group.Projects)) {
            $scanTarget = Resolve-ProjectEntry $entry
            $leaf = Split-Path -Leaf $scanTarget.TrimEnd('\', '/')
            $folder = "site-" + (Get-Slug $leaf)

            # Per-project source link: an explicit SourceLinkType/Base/Ref on the entry wins;
            # otherwise for a GitLab entry, auto-derive the HTTPS repo URL from its own
            # GitLabUrl (each project has a different path, so the global config value
            # can't cover more than one repo in the group).
            $linkType = $null; $linkBase = $null; $linkRef = $null
            if ($entry -isnot [string]) {
                if ($entry.SourceLinkType) { $linkType = [string]$entry.SourceLinkType }
                if ($entry.SourceLinkBase) { $linkBase = [string]$entry.SourceLinkBase }
                if ($entry.SourceLinkRef)  { $linkRef  = [string]$entry.SourceLinkRef }
                if (-not $linkBase -and $entry.GitLabUrl) {
                    $derived = ConvertTo-HttpsRepoUrl ([string]$entry.GitLabUrl)
                    if ($derived) {
                        $linkBase = $derived
                        if (-not $linkType) { $linkType = 'gitlab' }
                        if (-not $linkRef -and $entry.GitRef) { $linkRef = [string]$entry.GitRef }
                    }
                }
            }

            Invoke-ScanProject $scanTarget (Join-Path $LauncherDir $folder) $linkType $linkBase $linkRef
            $siteFolders += $folder
        }
        Write-Head "Group landscape: $($group.Name)"
        $lOut = Join-Path $LauncherDir ("site-landscape-" + $groupSlug)
        Write-Info "Output: $lOut"
        & dotnet run --project $ToolProject -- --landscape $LauncherDir --out $lOut --title $group.Name --only ($siteFolders -join ',') --no-open
        if ($LASTEXITCODE -ne 0) { Fail "Group landscape '$($group.Name)' failed with code $LASTEXITCODE." }
    }

    if ($cfg.OverallLandscape) {
        Write-Head "Overall landscape (all groups)"
        $lOut = Join-Path $LauncherDir 'site-landscape'
        Write-Info "Output: $lOut"
        $overallArgs = @('run', '--project', $ToolProject, '--', '--landscape', $LauncherDir, '--out', $lOut, '--title', 'All Sites')
        if (-not $OpenViewer) { $overallArgs += '--no-open' }
        & dotnet @overallArgs
        if ($LASTEXITCODE -ne 0) { Fail "Overall landscape failed with code $LASTEXITCODE." }
    }

    Write-Host ""
    Write-Host "Done." -ForegroundColor Green
    exit 0
}

Write-Head "ArchDiagram"

if (-not [string]::IsNullOrWhiteSpace($GitLabUrl)) {
    Write-Info "Source: GitLab -> $GitLabUrl"
    $scanTarget = Invoke-CloneOrUpdate $GitLabUrl $GitRef $WorkDir
} else {
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) { Fail "No source configured. Set ProjectPath or GitLabUrl in the config." }
    if (-not (Test-Path -LiteralPath $ProjectPath)) { Fail "ProjectPath does not exist: $ProjectPath" }
    $scanTarget = (Resolve-Path -LiteralPath $ProjectPath).Path
    Write-Info "Source: local path -> $scanTarget"
}

# Auto-name the output folder 'site-<project>' when none was configured, so
# generated sites for different sources can coexist in the launcher folder.
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $leaf = Split-Path -Leaf $scanTarget.TrimEnd('\', '/')
    $OutputDir = Join-Path $LauncherDir ("site-" + (Get-Slug $leaf))
} else {
    $OutputDir = Resolve-PathMaybeRelative $OutputDir $LauncherDir
}

Write-Info "Output: $OutputDir"

# ---- Run --------------------------------------------------------------------

$toolArgs = @('run', '--project', $ToolProject, '--', $scanTarget, '--out', $OutputDir)
if ($MaxNodes -gt 0) { $toolArgs += @('--max-nodes', $MaxNodes) }
if (-not $OpenViewer) { $toolArgs += '--no-open' }
if (-not $Complexity) { $toolArgs += '--no-complexity' }
if (-not $Snippets)   { $toolArgs += '--no-snippets' }
if (-not $Wiki)       { $toolArgs += '--no-wiki' }
$toolArgs = Add-CommonScanArgs $toolArgs

& dotnet @toolArgs
$code = $LASTEXITCODE
if ($code -ne 0) { Fail "archdiagram exited with code $code. See output above." }

Write-Host ""
Write-Host "Done." -ForegroundColor Green
