@echo off
setlocal enabledelayedexpansion
title Cisco IOS-XE Software Upgrade Orchestrator

echo ======================================================================
echo  Cisco IOS-XE Software Upgrade Orchestrator - Native Desktop Launcher
echo ======================================================================
echo.

:: 1. Locate .NET SDK / Runtime
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;!PATH!"
)

where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET 8 Runtime or SDK not found on PATH.
    echo Please install .NET 8 SDK or Runtime from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: 2. Locate or initialize Python virtual environment
if not exist ".venv\Scripts\python.exe" (
    echo [INFO] Python virtual environment not found at .venv. Initializing...
    where uv >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        uv venv .venv
        .venv\Scripts\python.exe -m pip install -r requirements.txt
    ) else (
        python -m venv .venv
        .venv\Scripts\python.exe -m pip install -r requirements.txt
    )
)

:: 3. Launch GUI
echo [INFO] Starting Cisco Upgrade Orchestrator Desktop GUI...
if exist "bin\publish\CiscoUpgradeOrchestrator.exe" (
    start "" "bin\publish\CiscoUpgradeOrchestrator.exe"
) else if exist "bin\publish\CiscoUpgradeOrchestrator.Gui.exe" (
    start "" "bin\publish\CiscoUpgradeOrchestrator.Gui.exe"
) else (
    dotnet run --project gui\CiscoUpgradeOrchestrator.Gui\CiscoUpgradeOrchestrator.Gui.csproj -c Release
)
