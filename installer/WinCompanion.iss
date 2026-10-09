; Inno Setup script for WinCompanion. Build it with build\build-release.ps1 (which publishes the app first).
; Installs for the current user only, so no administrator rights or UAC prompt are needed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{B6F3A4D2-7C1E-4E5B-9A57-3D2E8F6C1A90}
AppName=Tessa
AppVersion={#AppVersion}
AppVerName=Tessa {#AppVersion}
AppPublisher=Elanchezhiyan P
AppPublisherURL=https://codebyelan.in
AppSupportURL=https://codebyelan.in
AppContact=elanche97@gmail.com
AppCopyright=Copyright (c) 2026 Elanchezhiyan P
VersionInfoVersion={#AppVersion}
; Per-user install: with PrivilegesRequired=lowest, {autopf} is %LocalAppData%\Programs.
DefaultDirName={autopf}\WinCompanion
DefaultGroupName=Tessa
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=WinCompanion-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\WinCompanion.exe
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Refuse to install over a running copy (the app holds this mutex), and ask the user to quit it first.
AppMutex=WinCompanion.SingleInstance

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start Tessa when I sign in to Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\Pets\CREDITS.md"; DestDir: "{app}"; DestName: "CREDITS.md"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Tessa"; Filename: "{app}\WinCompanion.exe"
Name: "{autodesktop}\Tessa"; Filename: "{app}\WinCompanion.exe"; Tasks: desktopicon

[Registry]
; Same entry the app's own "Start with Windows" switch writes, so the two stay in step.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinCompanion"; \
    ValueData: """{app}\WinCompanion.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\WinCompanion.exe"; Description: "Launch Tessa"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel=Tessa is installed. Your chats, settings and reminders live in %AppData%\WinCompanion. When you uninstall, you can choose to delete them too.

[Code]
// Uninstalling removes the program. Personal data (chats, settings, the encrypted Gemini key, reminders) is only
// deleted if the user says so; a silent uninstall always keeps it.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Also delete your Tessa data (chats, settings, saved Gemini key, reminders)?' + #13#10 + #13#10 +
              'Choose No to keep it for a future install.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\WinCompanion'), True, True, True);
end;
