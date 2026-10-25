; =========================================================================
; HeliumSecure - Inno Setup 6 Script
; Produces dist\HeliumSecure-Setup.exe (Per-user, standard Windows Setup Wizard)
; =========================================================================

#define MyAppName "HeliumSecure"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "The HeliumSecure Authors"
#define MyAppURL "https://github.com/UTKARSHKUMAR712/Helium_secure"
#define MyAppExeName "HeliumSecure.exe"
#define MyAppId "{{B8F4A836-7C8D-4FA1-8D99-EE9A8E7C4124}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default per-user installation in %LOCALAPPDATA%\Programs\HeliumSecure
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output configuration
OutputDir=..\dist
OutputBaseFilename=HeliumSecure-Setup
SetupIconFile=..\src\HeliumSecure\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=4

; Architecture: 64-bit Windows
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; UI and wizard appearance
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
CloseApplicationsFilter=HeliumSecure.exe

; Windows Uninstall metadata
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Copy all published files and bundled browser dependencies from staging
Source: "..\dist\HeliumSecure\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
; Desktop shortcut (opt-in)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Option to launch after installation
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent

[Code]
// Custom Inno Setup Pascal script to ensure running instances are closed cleanly
function InitializeUninstall(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  Exec('taskkill.exe', '/F /IM HeliumSecure.exe', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;
