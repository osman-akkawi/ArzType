; ArzType installer (Inno Setup 6) — builds ArzType-Setup.exe
; Per-user install: no administrator rights needed, nothing written outside the user's profile.
; Build: scripts\make-installer.cmd   (or: ISCC.exe installer\ArzType.iss)

#define AppName "ArzType"
#ifndef AppVersion
  #define AppVersion "0.2.1"
#endif
#define AppExe "ArzType.exe"

[Setup]
AppId={{6F3B8E2A-7C1D-4E5B-9A2F-41525A54A001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ArzType contributors
AppComments=Fixes text typed with the wrong keyboard layout — offline, 70+ languages.
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
OutputDir=..\publish
OutputBaseFilename=ArzType-Setup-{#AppVersion}
SetupIconFile=..\src\ArzType.App\arztype.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
; Close a running ArzType before upgrading/uninstalling (matches the app's single-instance mutex).
AppMutex=Local\ArzType.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "Arabic.isl"

[CustomMessages]
english.StartupTask=Start ArzType automatically with Windows
arabic.StartupTask=شغّل ArzType تلقائيًا مع ويندوز
english.DesktopTask=Create a desktop shortcut
arabic.DesktopTask=أنشئ اختصارًا على سطح المكتب
english.LaunchNow=Start ArzType now
arabic.LaunchNow=شغّل ArzType الآن

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"
Name: "desktopicon"; Description: "{cm:DesktopTask}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.ar.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\DATA-LICENSES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app writes from its "Start with Windows" option, so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ArzType"; \
  ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Removes settings and learned-word fingerprints on uninstall.
Type: filesandordirs; Name: "{localappdata}\ArzType"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ArzType');
end;
