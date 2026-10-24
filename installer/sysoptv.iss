; SysopTV, Copyright (C) 2026 Christoph Scheel
; Licensed under the GNU General Public License v3.0, see LICENSE.
;
; Setup for the Windows program. Built by build-app.sh, which passes the
; version from app/SysopTV.Tray/SysopTV.Tray.csproj and the version of the
; firmware that comes with it (dist/sysoptv-<version>.bin):
;   ISCC.exe /DAppVersion=0.8.0 /DFirmwareVersion=0.5.0 installer\sysoptv.iss
; Installs without administrator rights to %LOCALAPPDATA%\Programs\SysopTV.

#ifndef AppVersion
  #error AppVersion fehlt, bitte mit build-app.sh bauen
#endif
#ifndef FirmwareVersion
  #error FirmwareVersion fehlt, bitte mit build-app.sh bauen
#endif

#define AppName "SysopTV"
#define AppExe "SysopTV.exe"
#define BinDir "..\app\SysopTV.Tray\bin\Release\net48"

[Setup]
AppId={{6F1B2C3D-5E4A-4B7C-9D8E-0A1B2C3D4E5F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Christoph Scheel
AppPublisherURL=https://github.com/alph4r79/SysopTV
AppSupportURL=https://github.com/alph4r79/SysopTV
PrivilegesRequired=lowest
DefaultDirName={userpf}\{#AppName}
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=sysoptv-app-{#AppVersion}-setup
SetupIconFile=..\app\assets\sysoptv.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The running program holds this mutex ("Local\SysopTV-App"), setup and
; uninstall then ask to close it first.
AppMutex=SysopTV-App
CloseApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "autostart"; Description: "Mit Windows starten"; GroupDescription: "Weitere Optionen:"

[InstallDelete]
; Firmware files of an earlier version, the program takes the newest anyway.
Type: files; Name: "{app}\firmware\sysoptv-*.bin"

[Files]
Source: "{#BinDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\app\assets\fonts\OFL-Inter.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\app\assets\fonts\OFL-JetBrainsMono.txt"; DestDir: "{app}"; Flags: ignoreversion
; For "Firmware aktualisieren" on the page Würfel, nothing is downloaded.
Source: "..\dist\sysoptv-{#FirmwareVersion}.bin"; DestDir: "{app}\firmware"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Same value as "Mit Windows starten" in the program.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SysopTV"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "SysopTV jetzt starten"; Flags: nowait postinstall skipifsilent
; The update from the program runs the setup silently with /STARTAPP=1 and
; gets the program back afterwards.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: StartAfterUpdate

[Code]
function StartAfterUpdate(): Boolean;
begin
  Result := WizardSilent() and (ExpandConstant('{param:STARTAPP|0}') = '1');
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    MsgBox('SysopTV braucht das .NET Framework 4.8. Es ist bei Windows 10 (ab Version 1903) und Windows 11 normalerweise schon dabei.' + #13#10#13#10 +
           'Bitte installiere es über Windows Update oder von der Seite von Microsoft und starte das Setup dann noch einmal.',
           mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { The program may have set the autostart value itself, remove it in any case. }
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SysopTV');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Sollen auch die Einstellungen und das Log von SysopTV gelöscht werden?',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      { Only the files of this program. The folder "alt" with moved shortcuts
        and the log of the old PowerShell script stay. }
      DeleteFile(ExpandConstant('{userappdata}\SysopTV\config.json'));
      DeleteFile(ExpandConstant('{userappdata}\SysopTV\config.bak.json'));
      DeleteFile(ExpandConstant('{userappdata}\SysopTV\config.broken.json'));
      DeleteFile(ExpandConstant('{localappdata}\SysopTV\app.log'));
      DeleteFile(ExpandConstant('{localappdata}\SysopTV\app.old.log'));
      DeleteFile(ExpandConstant('{localappdata}\SysopTV\state.json'));
      RemoveDir(ExpandConstant('{userappdata}\SysopTV'));
      RemoveDir(ExpandConstant('{localappdata}\SysopTV'));
    end;
  end;
end;
