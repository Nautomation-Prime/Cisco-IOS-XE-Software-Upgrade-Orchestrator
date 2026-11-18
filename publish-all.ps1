<#
.SYNOPSIS
    Compiles, bundles, verifies, and produces complete standalone release artifacts for Cisco IOS-XE Upgrade Orchestrator.
.DESCRIPTION
    Runs full pre-flight tests and audits, builds self-contained .NET 8 single-file desktop binaries,
    assembles and smoke-tests an embedded relocatable Python 3.12 runtime with all networking packages
    (zero .NET or Python prerequisites required on target machines), compiles the standalone Inno Setup
    installer, packages the standalone portable ZIP, computes SHA-256 checksums, and emits update_manifest.json.
.EXAMPLE
    .\publish-all.ps1 -Version 1.0.0
    .\publish-all.ps1 -Version 1.0.0 -Target all
    .\publish-all.ps1 -Version 1.0.0 -SkipTests
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [ValidateSet("win", "all")]
    [string]$Target = "win",
    [switch]$SkipTests,
    [switch]$AllowDevelopmentCertificate
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$DistDir = Join-Path $RepoRoot "dist"
$BinStandalone = Join-Path $RepoRoot "bin\standalone-win-x64"
$BinPythonRuntime = Join-Path $RepoRoot "bin\python-runtime"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  Cisco IOS-XE Upgrade Orchestrator - Standalone Release Builder" -ForegroundColor Cyan
Write-Host "  Target Version : $Version" -ForegroundColor Yellow
Write-Host "  Target Platform: $Target" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan

# Locate .NET
if (Test-Path "$env:LocalAppData\Microsoft\dotnet\dotnet.exe") {
    $env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

# 1. Pre-Flight Verification
if (-not $SkipTests) {
    Write-Host "`n>>> [1/6] Running Pre-Flight Tests and Codebase Audits..." -ForegroundColor Cyan
    & "$RepoRoot\scripts\test-all.ps1"
    & "$RepoRoot\scripts\audit-branding.ps1"
} else {
    Write-Host "`n>>> [1/6] Skipping tests as requested (-SkipTests)." -ForegroundColor Yellow
}

# 2. Version Preparation
Write-Host "`n>>> [2/6] Preparing Release Metadata ($Version)..." -ForegroundColor Cyan
& "$RepoRoot\scripts\prepare-release.ps1" -Version $Version

# Ensure dist directory is ready and clean old artifacts
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}
Get-ChildItem -Path $DistDir -File | Remove-Item -Force

# 3. Compile & Publish Self-Contained GUI Binaries
Write-Host "`n>>> [3/6] Compiling Self-Contained GUI Binaries (Release mode)..." -ForegroundColor Cyan
$guiProj = Join-Path $RepoRoot "gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj"

if (Test-Path $BinStandalone) { Remove-Item -Path $BinStandalone -Recurse -Force }

Write-Host "Building standalone self-contained win-x64 single-file binary..." -ForegroundColor Gray
& dotnet publish $guiProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $BinStandalone
if ($LASTEXITCODE -ne 0) { Write-Error "Dotnet publish (standalone win-x64) failed." }

# Remove debug PDB from standalone output
Get-ChildItem -Path $BinStandalone -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

$standaloneSrc = Join-Path $BinStandalone "CiscoUpgradeOrchestrator.exe"
if (-not (Test-Path $standaloneSrc)) {
    Write-Error "Standalone executable not found at $standaloneSrc"
}

Copy-Item -Path $standaloneSrc -Destination (Join-Path $DistDir "CiscoUpgradeOrchestrator.exe") -Force
Copy-Item -Path $standaloneSrc -Destination (Join-Path $DistDir "CiscoUpgradeOrchestrator_v$Version.exe") -Force
Write-Host "[OK] Standalone win-x64 binary ready: dist\CiscoUpgradeOrchestrator.exe" -ForegroundColor Green

# Optional cross-platform standalone binaries when -Target all is passed
if ($Target -eq "all") {
    foreach ($rid in @("osx-arm64", "osx-x64", "linux-x64")) {
        $outDir = Join-Path $RepoRoot "bin\standalone-$rid"
        if (Test-Path $outDir) { Remove-Item -Path $outDir -Recurse -Force }
        Write-Host "Building standalone self-contained $rid binary..." -ForegroundColor Gray
        & dotnet publish $guiProj -c Release -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir
        if ($LASTEXITCODE -ne 0) { Write-Error "Dotnet publish ($rid) failed." }
        Get-ChildItem -Path $outDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force

        $crossZip = Join-Path $DistDir "Cisco-IOS-XE-Software-Upgrade-Orchestrator-v$Version-$rid.zip"
        $crossStage = Join-Path $RepoRoot "bin\staging-$rid"
        if (Test-Path $crossStage) { Remove-Item -Path $crossStage -Recurse -Force }
        New-Item -ItemType Directory -Path $crossStage | Out-Null
        Copy-Item -Path "$outDir\*" -Destination $crossStage -Recurse -Force
        Copy-Item -Path (Join-Path $RepoRoot "gui_bridge.py"), (Join-Path $RepoRoot "orchestrator.py"), (Join-Path $RepoRoot "requirements.txt"), (Join-Path $RepoRoot "LICENSE"), (Join-Path $RepoRoot "NOTICE"), (Join-Path $RepoRoot "README.md") -Destination $crossStage -Force
        robocopy (Join-Path $RepoRoot "src") (Join-Path $crossStage "src") /E /XD __pycache__ /XF *.pyc *.pyo /NFL /NDL /NJH /NJS /NP | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $crossStage "config"), (Join-Path $crossStage "inventory") | Out-Null
        Copy-Item -Path (Join-Path $RepoRoot "config\config.example.yaml") -Destination (Join-Path $crossStage "config") -Force
        Copy-Item -Path (Join-Path $RepoRoot "inventory\inventory_template.xlsx") -Destination (Join-Path $crossStage "inventory") -Force
        Compress-Archive -Path "$crossStage\*" -DestinationPath $crossZip -Force
        Remove-Item -Path $crossStage -Recurse -Force
        Write-Host "[OK] Packaged $rid archive: $crossZip" -ForegroundColor Green
    }
}

# 4. Assemble & Smoke-Test Embedded Standalone Python Runtime (Windows x64)
Write-Host "`n>>> [4/6] Assembling Embedded Standalone Python Runtime..." -ForegroundColor Cyan
$venvPython = Join-Path $RepoRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    Write-Error "Project virtual environment not found at $venvPython. Create .venv with requirements.txt installed first."
}

$pyInfoJson = & $venvPython -c "import sys, json; print(json.dumps({'base': sys.base_prefix, 'site': next(p for p in sys.path if 'site-packages' in p)}))"
$pyInfo = $pyInfoJson | ConvertFrom-Json
$basePythonDir = $pyInfo.base
$venvSiteDir = $pyInfo.site

if (-not (Test-Path (Join-Path $basePythonDir "python.exe"))) {
    Write-Error "Base Python directory invalid: $basePythonDir"
}

if (Test-Path $BinPythonRuntime) { Remove-Item -Path $BinPythonRuntime -Recurse -Force }
New-Item -ItemType Directory -Path $BinPythonRuntime | Out-Null

Write-Host "Copying relocatable CPython base from $basePythonDir (pruning dev/test/tcl bloat)..." -ForegroundColor Gray
robocopy $basePythonDir $BinPythonRuntime /E `
    /XD tcl include libs Doc Tools test tests tkinter idlelib ensurepip venv pydoc_data turtledemo __pycache__ `
    /XF *.pyc *.pyo *.pdb *.chm `
    /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { Write-Error "Robocopy failed copying base Python runtime (exit $LASTEXITCODE)." }

$bundledSiteDir = Join-Path $BinPythonRuntime "Lib\site-packages"
New-Item -ItemType Directory -Path $bundledSiteDir -Force | Out-Null
Write-Host "Copying verified Python dependencies from $venvSiteDir..." -ForegroundColor Gray
robocopy $venvSiteDir $bundledSiteDir /E `
    /XD __pycache__ pip pip-* setuptools setuptools-* wheel wheel-* _pytest pytest* `
    /XF *.pyc *.pyo *.pdb `
    /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { Write-Error "Robocopy failed copying site-packages (exit $LASTEXITCODE)." }

# Prune non-Cisco ntc_templates (removes ~1,500 unused vendor templates and prevents Windows MAX_PATH hits in Inno Setup)
$ntcTemplatesDir = Join-Path $bundledSiteDir "ntc_templates\templates"
if (Test-Path $ntcTemplatesDir) {
    Get-ChildItem -Path $ntcTemplatesDir -Filter "*.textfsm" |
        Where-Object { $_.Name -notlike "cisco_*" } |
        Remove-Item -Force
}

# Smoke-test the bundled Python runtime completely isolated from host Python/user site
$bundledExe = Join-Path $BinPythonRuntime "python.exe"
$prevNoUserSite = $env:PYTHONNOUSERSITE
$prevPyHome = $env:PYTHONHOME
$env:PYTHONNOUSERSITE = "1"
Remove-Item Env:PYTHONHOME -ErrorAction SilentlyContinue
try {
    $envCheckRaw = & $bundledExe (Join-Path $RepoRoot "gui_bridge.py") test-env
    $envCheck = $envCheckRaw | ConvertFrom-Json
    if (-not $envCheck.all_installed) {
        Write-Error "Bundled python-runtime smoke test FAILED! Missing dependencies: $($envCheck.import_error)"
    }
    # Verify actual import of core runtime modules (netmiko, nornir, openpyxl, click, yaml, rich)
    & $bundledExe -c "import nornir, netmiko, paramiko, scp, openpyxl, rich, click, keyring, yaml, dotenv; print('IMPORTS_OK')"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Bundled python-runtime deep import verification FAILED!"
    }
} finally {
    if ($null -ne $prevNoUserSite) { $env:PYTHONNOUSERSITE = $prevNoUserSite } else { Remove-Item Env:PYTHONNOUSERSITE -ErrorAction SilentlyContinue }
    if ($null -ne $prevPyHome) { $env:PYTHONHOME = $prevPyHome }
}

$pyRuntimeMb = [Math]::Round(((Get-ChildItem $BinPythonRuntime -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "[OK] Standalone embedded Python runtime verified ($pyRuntimeMb MB uncompressed, all dependencies active)." -ForegroundColor Green

# 5. Packaging (Standalone Inno Setup Installer & Standalone Portable ZIP)
Write-Host "`n>>> [5/6] Packaging Standalone Installer & Portable ZIP..." -ForegroundColor Cyan

# Locate Inno Setup Compiler
$isccPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) { $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $isccPath)) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

$installerExe = Join-Path $DistDir "CiscoUpgradeOrchestrator_Setup_v$Version.exe"
if (Test-Path $isccPath) {
    Write-Host "Compiling standalone Windows installer with Inno Setup ($isccPath)..." -ForegroundColor Gray
    $issFile = Join-Path $RepoRoot "scripts\installer.iss"
    & $isccPath "/DMyAppVersion=$Version" $issFile
    if ($LASTEXITCODE -eq 0 -and (Test-Path $installerExe)) {
        Copy-Item -Path $installerExe -Destination (Join-Path $DistDir "CiscoUpgradeOrchestrator_Setup.exe") -Force
        Write-Host "[OK] Standalone Inno Setup installer compiled: $installerExe" -ForegroundColor Green
    } else {
        Write-Error "Inno Setup compilation failed (exit $LASTEXITCODE)."
    }
} else {
    Write-Warning "Inno Setup 6 compiler (ISCC.exe) not found. Skipping installer generation."
}

# Create Standalone Portable ZIP Archive (Self-Contained .NET + Embedded Python Runtime)
$zipName = "Cisco-IOS-XE-Software-Upgrade-Orchestrator-v$Version-win-x64.zip"
$zipPath = Join-Path $DistDir $zipName
Write-Host "Creating complete standalone portable ZIP archive: $zipName..." -ForegroundColor Gray

$stagingDir = Join-Path $RepoRoot "bin\staging-zip"
if (Test-Path $stagingDir) { Remove-Item -Path $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir | Out-Null

# Copy Self-Contained .NET GUI (NOT framework-dependent bin\publish!)
Copy-Item -Path "$BinStandalone\*" -Destination $stagingDir -Recurse -Force

# Copy Embedded Standalone Python Runtime
robocopy $BinPythonRuntime (Join-Path $stagingDir "python-runtime") /E /NFL /NDL /NJH /NJS /NP | Out-Null

# Copy Python Orchestrator Engine & Docs
Copy-Item -Path (Join-Path $RepoRoot "gui_bridge.py") -Destination $stagingDir -Force
Copy-Item -Path (Join-Path $RepoRoot "orchestrator.py") -Destination $stagingDir -Force
Copy-Item -Path (Join-Path $RepoRoot "requirements.txt") -Destination $stagingDir -Force
Copy-Item -Path (Join-Path $RepoRoot "LICENSE") -Destination $stagingDir -Force
Copy-Item -Path (Join-Path $RepoRoot "NOTICE") -Destination $stagingDir -Force
Copy-Item -Path (Join-Path $RepoRoot "README.md") -Destination $stagingDir -Force

robocopy (Join-Path $RepoRoot "src") (Join-Path $stagingDir "src") /E /XD __pycache__ /XF *.pyc *.pyo /NFL /NDL /NJH /NJS /NP | Out-Null

New-Item -ItemType Directory -Path (Join-Path $stagingDir "config"), (Join-Path $stagingDir "inventory"), (Join-Path $stagingDir "state") | Out-Null
Copy-Item -Path (Join-Path $RepoRoot "config\config.example.yaml") -Destination (Join-Path $stagingDir "config") -Force
Copy-Item -Path (Join-Path $RepoRoot "inventory\inventory_template.xlsx") -Destination (Join-Path $stagingDir "inventory") -Force

Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -Force
Remove-Item -Path $stagingDir -Recurse -Force
Write-Host "[OK] Complete standalone portable ZIP created: $zipPath" -ForegroundColor Green

# 6. Checksums, Version Verification & Update Manifest
Write-Host "`n>>> [6/6] Verifying Versions, Generating Checksums & Update Manifest..." -ForegroundColor Cyan

$exeVer = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $DistDir "CiscoUpgradeOrchestrator.exe")).FileVersion
Write-Host "[VERIFY] CiscoUpgradeOrchestrator.exe FileVersion = $exeVer" -ForegroundColor Gray
if (Test-Path (Join-Path $DistDir "CiscoUpgradeOrchestrator_Setup.exe")) {
    $setupVer = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $DistDir "CiscoUpgradeOrchestrator_Setup.exe")).FileVersion
    Write-Host "[VERIFY] CiscoUpgradeOrchestrator_Setup.exe FileVersion = $setupVer" -ForegroundColor Gray
}

$checksums = [System.Collections.Generic.List[string]]::new()
$distFiles = Get-ChildItem -Path $DistDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" -and $_.Name -ne "update_manifest.json" }

$manifestAssets = @{}

foreach ($file in $distFiles) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums.Add("$hash  $($file.Name)")
    $manifestAssets[$file.Name] = @{
        "sha256" = $hash
        "size_bytes" = $file.Length
        "file_name" = $file.Name
    }
}

$checksumsPath = Join-Path $DistDir "SHA256SUMS.txt"
Set-Content -Path $checksumsPath -Value ($checksums -join "`r`n") -Encoding utf8
Write-Host "[OK] Generated $checksumsPath" -ForegroundColor Green

$manifest = @{
    "version" = $Version
    "release_date" = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    "product" = "Cisco IOS-XE Software Upgrade Orchestrator"
    "standalone" = $true
    "bundled_runtimes" = @(".NET 8 (self-contained)", "Python 3.12 (embedded with all networking packages)")
    "channels" = @("stable")
    "assets" = $manifestAssets
    "release_notes" = "Cisco IOS-XE Software Upgrade Orchestrator v$Version complete standalone release: self-contained FluentAvalonia desktop GUI + embedded Python 3.12 runtime (zero external .NET or Python prerequisites), 4-stage pipeline stepper, card-based switch inventory with live validation, and OS-aware Windows/macOS navigation."
}

$manifestJson = $manifest | ConvertTo-Json -Depth 5
$manifestPath = Join-Path $DistDir "update_manifest.json"
Set-Content -Path $manifestPath -Value $manifestJson -Encoding utf8
Write-Host "[OK] Generated $manifestPath" -ForegroundColor Green

Write-Host "`n======================================================================" -ForegroundColor Green
Write-Host "  COMPLETE STANDALONE RELEASE BUILD SUCCESSFUL! (v$Version)" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
Write-Host "Artifacts available in: $DistDir" -ForegroundColor Cyan
Get-ChildItem -Path $DistDir | Select-Object Name, @{Name="SizeMB";Expression={[Math]::Round($_.Length/1MB,2)}}, LastWriteTime | Format-Table -AutoSize
