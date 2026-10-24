; =========================================================================
; HeliumSecure - Inno Setup 6 Script
; Produces dist\HeliumSecure-Setup.exe (Per-user, standard Windows Setup Wizard)
; =========================================================================

#define MyAppName "HeliumSecure"
#define MyAppVersion "1.0.0"
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
Compression=lzma2/max
SolidCompression=yes

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
UninstallDisplayIcon={app}\{#MyAppExeName},0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Package the complete application payload including launcher and bundled browser binaries
Source: "..\dist\HeliumSecure\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Guard against deleting user vaults or upstream Helium installations during uninstall
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Note: User vaults are stored in %LOCALAPPDATA%\HeliumSecure\Vaults
    // The installer only deletes the {app} binaries folder. Vaults remain 100% safe.
  end;
end;
