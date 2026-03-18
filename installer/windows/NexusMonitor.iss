; Nexus System Monitor — Inno Setup Installer Script
; =====================================================================
; Usage (CI — jrsoftware/iscc@v1):
;   iscc NexusMonitor.iss /DAppVersion=0.1.0 /DAppArch=x64
;        /DPublishDir=..\..\src\NexusMonitor.UI\publish\win-x64
;        /DOutputDir=..\..\dist
;        /DOutputFilename=NexusMonitor-Windows-Installer-0.1.0
;
; Usage (local — Inno Setup must be installed):
;   iscc installer\windows\NexusMonitor.iss /DAppVersion=0.1.0 /DAppArch=x64
;        /DPublishDir=src\NexusMonitor.UI\publish\win-x64
;        /DOutputDir=dist
;        /DOutputFilename=NexusMonitor-Windows-Installer-0.1.0
; =====================================================================

; Defaults for local/manual runs (CI passes these via /D flags)
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\src\NexusMonitor.UI\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif
#ifndef OutputFilename
  #if AppArch == "arm64"
    #define OutputFilename "NexusMonitor-Windows-ARM-Installer-" + AppVersion
  #else
    #define OutputFilename "NexusMonitor-Windows-Installer-" + AppVersion
  #endif
#endif

[Setup]
AppId={{B7C3A2E1-9F5D-4D8A-A1B2-C3D4E5F60001}
AppName=Nexus System Monitor
AppVersion={#AppVersion}
AppPublisher=TheBlackSwordsman
AppPublisherURL=https://github.com/brass458/nexus-system-monitor
AppSupportURL=https://github.com/brass458/nexus-system-monitor/issues
AppUpdatesURL=https://github.com/brass458/nexus-system-monitor/releases
DefaultDirName={autopf}\Nexus System Monitor
DefaultGroupName=Nexus System Monitor
AllowNoIcons=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#OutputFilename}
SetupIconFile=..\..\src\NexusMonitor.UI\Assets\nexus-icon.ico
UninstallDisplayIcon={app}\NexusMonitor.exe
UninstallDisplayName=Nexus System Monitor {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Architecture-specific settings
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
#endif
; Minimum Windows 10 1803 (required by .NET 8 + net8.0-windows10.0.17763.0)
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy all published files
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Nexus System Monitor"; Filename: "{app}\NexusMonitor.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,Nexus System Monitor}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Nexus System Monitor"; Filename: "{app}\NexusMonitor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\NexusMonitor.exe"; Description: "{cm:LaunchProgram,Nexus System Monitor}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Remove runtime data left by the app (settings, metrics DB)
Type: filesandordirs; Name: "{localappdata}\NexusMonitor"
