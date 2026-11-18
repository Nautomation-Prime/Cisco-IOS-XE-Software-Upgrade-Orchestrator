; Inno Setup 6 Script for Cisco IOS-XE Software Upgrade Orchestrator (Complete Standalone Installer)
#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif

#define MyAppName "Cisco IOS-XE Software Upgrade Orchestrator"
#define MyAppPublisher "Cisco Upgrade Orchestrator Project"
#define MyAppExeName "CiscoUpgradeOrchestrator.exe"

[Setup]
AppId={{8E5D7A19-952F-43B0-8C83-9366D38006C7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\CiscoUpgradeOrchestrator
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=CiscoUpgradeOrchestrator_Setup_v{#MyAppVersion}
SetupIconFile=..\gui\CiscoUpgradeOrchestrator.Gui\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableWelcomePage=no
DisableDirPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{app}\state"
Name: "{app}\inventory"
Name: "{app}\config"

[Files]
; Self-Contained .NET 8 Avalonia Desktop Application (No external .NET Runtime required)
Source: "..\bin\standalone-win-x64\CiscoUpgradeOrchestrator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\standalone-win-x64\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; Self-Contained Embedded Python 3.12 Runtime + All Dependencies (No external Python/pip required)
Source: "..\bin\python-runtime\*"; DestDir: "{app}\python-runtime"; Flags: ignoreversion recursesubdirs createallsubdirs

; Python Orchestration Engine & Default Configuration
Source: "..\gui_bridge.py"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\orchestrator.py"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\*"; DestDir: "{app}\src"; Excludes: "__pycache__,*.pyc,*.pyo"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\config\config.example.yaml"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "..\inventory\inventory_template.xlsx"; DestDir: "{app}\inventory"; Flags: ignoreversion
Source: "..\requirements.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
