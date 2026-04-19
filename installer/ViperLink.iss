#define MyAppId "ViperLink"
#define MyAppName "ViperLink"
#define MyAppExeName "ViperLink.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef MyPublishDir
  #define MyPublishDir "..\artifacts\publish\windows-nativeaot"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
AppPublisherURL=https://github.com/
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma
SolidCompression=yes
OutputDir=..\artifacts\release
OutputBaseFilename=ViperLink-windows-x64-setup-{#MyAppVersion}
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#MyPublishDir}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Launch {#MyAppName} when you sign in"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
var
  StartupTaskInitialized: Boolean;

procedure CurPageChanged(CurPageID: Integer);
var
  ExistingRunValue: string;
begin
  if StartupTaskInitialized or (CurPageID <> wpSelectTasks) then
  begin
    exit;
  end;

  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}', ExistingRunValue) then
  begin
    WizardSelectTasks('startup');
  end
  else
  begin
    WizardSelectTasks('!startup');
  end;

  StartupTaskInitialized := True;
end;
