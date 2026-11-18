<#
.SYNOPSIS
    Prepares release version metadata across Python and C# GUI files.
.DESCRIPTION
    Propagates version number into orchestrator.py, src/__init__.py, and CiscoUpgradeOrchestrator.Gui.csproj.
.EXAMPLE
    .\scripts\prepare-release.ps1 -Version 1.0.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Preparing Release Metadata for Version $Version" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 1. Update C# GUI .csproj
$csprojPath = Join-Path $RepoRoot "gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj"
if (Test-Path $csprojPath) {
    $csprojXml = Get-Content $csprojPath -Raw
    if ($csprojXml -match "<Version>.*?</Version>") {
        $csprojXml = $csprojXml -replace "<Version>.*?</Version>", "<Version>$Version</Version>"
    } else {
        $csprojXml = $csprojXml -replace "</PropertyGroup>", "    <Version>$Version</Version>`r`n  </PropertyGroup>"
    }
    Set-Content -Path $csprojPath -Value $csprojXml -NoNewline
    Write-Host "[OK] Updated $csprojPath with Version $Version" -ForegroundColor Green
}

# 2. Update orchestrator.py version_option
$orchestratorPy = Join-Path $RepoRoot "orchestrator.py"
if (Test-Path $orchestratorPy) {
    $orchContent = Get-Content $orchestratorPy -Raw
    $orchContent = $orchContent -replace '@click\.version_option\(version=".*?"\)', "@click.version_option(version=""$Version"")"
    Set-Content -Path $orchestratorPy -Value $orchContent -NoNewline
    Write-Host "[OK] Updated $orchestratorPy click version to $Version" -ForegroundColor Green
}

# 3. Update src/__init__.py
$srcInit = Join-Path $RepoRoot "src\__init__.py"
if (Test-Path $srcInit) {
    $initContent = Get-Content $srcInit -Raw
    $initContent = $initContent -replace '__version__\s*=\s*".*?"', "__version__ = ""$Version"""
    Set-Content -Path $srcInit -Value $initContent -NoNewline
    Write-Host "[OK] Updated $srcInit __version__ to $Version" -ForegroundColor Green
}

Write-Host "`n[SUCCESS] Version $Version prepared across all projects." -ForegroundColor Green
