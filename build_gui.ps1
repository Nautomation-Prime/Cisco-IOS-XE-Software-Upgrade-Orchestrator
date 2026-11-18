# build_gui.ps1 - Builds and publishes the Cisco Upgrade Orchestrator GUI
[CmdletBinding()]
param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

if (Test-Path "$env:LocalAppData\Microsoft\dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Building Cisco IOS-XE Upgrade Orchestrator GUI" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$projectPath = "$PSScriptRoot\gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj"

if ($SelfContained) {
    $outDir = "$PSScriptRoot\bin\standalone-win-x64"
    Write-Host "[INFO] Publishing self-contained win-x64 binary to: $outDir" -ForegroundColor Yellow
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $outDir
} else {
    $outDir = "$PSScriptRoot\bin\publish"
    Write-Host "[INFO] Publishing framework-dependent binary to: $outDir" -ForegroundColor Green
    & dotnet publish $projectPath -c Release -o $outDir
}

Write-Host "[SUCCESS] Build complete! Executable is located in: $outDir" -ForegroundColor Green
