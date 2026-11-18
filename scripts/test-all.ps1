<#
.SYNOPSIS
    Runs all automated test suites for Cisco IOS-XE Software Upgrade Orchestrator.
.DESCRIPTION
    Executes Python backend unit tests and Avalonia UI Headless XUnit tests.
.EXAMPLE
    .\scripts\test-all.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Cisco IOS-XE Orchestrator - Full Automated Test Suite" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 1. Run Python Unit Tests
Write-Host "`n[1/2] Running Python Backend Unit Tests..." -ForegroundColor Yellow
$pyExe = Join-Path $RepoRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $pyExe)) {
    $pyExe = "python"
}

Push-Location $RepoRoot
try {
    & $pyExe -m unittest discover tests
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Python unit tests failed with exit code $LASTEXITCODE"
    }
    Write-Host "[PASS] Python unit tests completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}

# 2. Run Avalonia Headless GUI Tests
Write-Host "`n[2/2] Running Avalonia UI Headless Unit Tests..." -ForegroundColor Yellow
if (Test-Path "$env:LocalAppData\Microsoft\dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

$guiTestProj = Join-Path $RepoRoot "gui\CiscoUpgradeOrchestrator.Gui.Tests\CiscoUpgradeOrchestrator.Gui.Tests.csproj"
if (Test-Path $guiTestProj) {
    & dotnet test $guiTestProj --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Avalonia GUI headless tests failed with exit code $LASTEXITCODE"
    }
    Write-Host "[PASS] Avalonia UI Headless tests completed successfully." -ForegroundColor Green
}
else {
    Write-Warning "GUI test project not found at $guiTestProj"
}

Write-Host "`n======================================================================" -ForegroundColor Green
Write-Host "  ALL TEST SUITES PASSED (100% OK)" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
