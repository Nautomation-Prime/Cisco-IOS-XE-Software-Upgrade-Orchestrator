<#
.SYNOPSIS
    Cleans build artifacts and caches from the workspace.
.DESCRIPTION
    Purges temporary .NET bin/obj folders and Python bytecode caches while preserving dist/.
.EXAMPLE
    .\scripts\clean-workspace.ps1
    .\scripts\clean-workspace.ps1 -CleanDist
#>

[CmdletBinding()]
param(
    [switch]$CleanDist
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Cleaning Workspace Caches and Build Artifacts" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 1. Clean .NET bin & obj directories
Get-ChildItem -Path $RepoRoot -Include "bin", "obj" -Recurse -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.FullName -notmatch "dist") {
        Write-Host "Removing: $($_.FullName)" -ForegroundColor Gray
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# 2. Clean Python __pycache__
Get-ChildItem -Path $RepoRoot -Include "__pycache__" -Recurse -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Removing: $($_.FullName)" -ForegroundColor Gray
    Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

# 3. Clean dist if requested
if ($CleanDist) {
    $distDir = Join-Path $RepoRoot "dist"
    if (Test-Path $distDir) {
        Write-Host "Removing: $distDir" -ForegroundColor Yellow
        Remove-Item -Path $distDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n[CLEAN COMPLETE] Workspace is clean." -ForegroundColor Green
