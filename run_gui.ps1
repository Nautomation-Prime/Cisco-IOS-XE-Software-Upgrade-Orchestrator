# run_gui.ps1 - Native Windows PowerShell Launcher for Cisco IOS-XE Software Upgrade Orchestrator GUI
[CmdletBinding()]
param(
    [switch]$Dev,
    [switch]$Build
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Cisco IOS-XE Software Upgrade Orchestrator - Desktop GUI Launcher" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# Locate .NET
if (Test-Path "$env:LocalAppData\Microsoft\dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

try {
    $dotnetVer = & dotnet --version 2>$null
    Write-Host "[OK] .NET SDK / Runtime detected: $dotnetVer" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] .NET 8 not found on PATH or in $env:LocalAppData\Microsoft\dotnet." -ForegroundColor Red
    Write-Host "Please install .NET 8 SDK or Runtime: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

# Check Python environment
$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (Test-Path $venvPython) {
    Write-Host "[OK] Python Virtual Environment found: $venvPython" -ForegroundColor Green
} else {
    Write-Host "[WARN] .venv virtual environment not found in project root." -ForegroundColor Yellow
    Write-Host "Attempting to create .venv using Python / UV..." -ForegroundColor Gray
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        & uv venv .venv
        & .venv\Scripts\python.exe -m pip install -r requirements.txt
    } else {
        & python -m venv .venv
        & .venv\Scripts\python.exe -m pip install -r requirements.txt
    }
}

# Run GUI
$publishExe = Join-Path $PSScriptRoot "bin\publish\CiscoUpgradeOrchestrator.exe"
if (-not (Test-Path $publishExe)) {
    $publishExe = Join-Path $PSScriptRoot "bin\publish\CiscoUpgradeOrchestrator.Gui.exe"
}
if ($Build -or (-not (Test-Path $publishExe))) {
    Write-Host "[INFO] Building/Publishing GUI..." -ForegroundColor Cyan
    & dotnet publish "$PSScriptRoot\gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj" -c Release -o "$PSScriptRoot\bin\publish"
}

if ($Dev) {
    Write-Host "[INFO] Launching in Dev mode (dotnet run)..." -ForegroundColor Cyan
    & dotnet run --project "$PSScriptRoot\gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj"
} else {
    Write-Host "[INFO] Launching Windows Desktop Application ($publishExe)..." -ForegroundColor Green
    Start-Process $publishExe
}
