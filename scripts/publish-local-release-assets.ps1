<#
.SYNOPSIS
    Uploads and publishes production release assets to GitHub.
.DESCRIPTION
    Validates dist/ artifacts, creates a git tag, drafts a release on GitHub,
    uploads assets, and publishes the release.
    Guarded with -ConfirmPublish to prevent accidental execution.
.EXAMPLE
    .\scripts\publish-local-release-assets.ps1 -Version 1.0.0 -ConfirmPublish
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$ConfirmPublish,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$DistDir = Join-Path $RepoRoot "dist"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  GitHub Production Release Publisher" -ForegroundColor Cyan
Write-Host "  Target Version: $Version" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan

if (-not $ConfirmPublish) {
    Write-Warning "Safe guard triggered: -ConfirmPublish switch is required to publish to GitHub."
    Write-Host "Run with -ConfirmPublish to proceed after verifying dist/ artifacts." -ForegroundColor Yellow
    exit 0
}

# 1. Check dist/ artifacts
$manifestPath = Join-Path $DistDir "update_manifest.json"
$checksumsPath = Join-Path $DistDir "SHA256SUMS.txt"
if (-not (Test-Path $manifestPath) -or -not (Test-Path $checksumsPath)) {
    Write-Error "dist/ directory is missing update_manifest.json or SHA256SUMS.txt. Run .\publish-all.ps1 first."
}

# 2. Check GitHub CLI (gh)
$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghCmd) {
    Write-Error "GitHub CLI ('gh') is required to publish release assets. Install gh or publish via GitHub web UI."
}

# 3. Create Git Tag
$tagName = "v$Version"
Write-Host "Checking git tag $tagName..." -ForegroundColor Gray
$tagExists = & git tag -l $tagName
if (-not $tagExists) {
    Write-Host "Creating git tag $tagName..." -ForegroundColor Cyan
    & git tag -a $tagName -m "Release $tagName"
    Write-Host "Pushing git tag $tagName to origin..." -ForegroundColor Cyan
    & git push origin $tagName
}

# 4. Prepare asset list
$assets = Get-ChildItem -Path $DistDir -File | ForEach-Object { $_.FullName }

# 5. Create GitHub Release
$notes = "Production Release $tagName featuring Avalonia 11 Fluent GUI and Python automation engine."
$ghArgs = @("release", "create", $tagName) + $assets + @("--title", "Release $tagName", "--notes", $notes)

if ($Draft) {
    $ghArgs += "--draft"
}

Write-Host "Publishing release via GitHub CLI..." -ForegroundColor Cyan
& gh @ghArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[SUCCESS] GitHub Release $tagName successfully published!" -ForegroundColor Green
} else {
    Write-Error "GitHub release creation failed with exit code $LASTEXITCODE."
}
