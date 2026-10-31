; TaskNova installer (Inno Setup 6.x).
;
; Build:
;   1. Publish the app:  pwsh -File build\publish.ps1 -SkipInstaller
;      This publishes directly into artifacts\tasknova_v<version>_portable\TaskNova (no separate publish folder).
;   2. Open this file in the Inno Setup Compiler and press Compile (Ctrl+F9) – it uses that portable folder as source
;      (the tasknova.portable marker and a data folder are left out, so the installed app stores its data per user).
;      Or run: ISCC /DAppVersion=0.0.4 /DSourceDir=..\..\artifacts\tasknova_v0.0.4_portable\TaskNova /DOutputDir=..\..\artifacts tasknova.iss
;   build\publish.ps1 does both steps (tests, publish, portable ZIP, setup, SHA-256).
;
; English is the default language; German is offered in the language dialog.
; Targets: Windows 10 / 11, Windows Server 2019 – 2025, 64-bit.

#ifndef AppVersion
  #define AppVersion "0.0.4"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\tasknova_v" + AppVersion + "_portable\TaskNova"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

[Setup]
AppId={{7A1F2C64-3B8E-4D9A-9E21-5C6B7D8E9F10}
AppName=TaskNova
AppVersion={#AppVersion}
AppVerName=TaskNova {#AppVersion}
AppPublisher=sksdesign | complicatiion
AppPublisherURL=https://sksdesign.de
AppSupportURL=https://github.com/complicatiion/TaskNova
AppCopyright=Copyright (C) 2026 Sven Klocke
DefaultDirName={autopf}\TaskNova
DefaultGroupName=TaskNova
DisableProgramGroupPage=yes
LicenseFile=..\..\licenses\LICENSE.md
OutputDir={#OutputDir}
OutputBaseFilename=tasknova_v{#AppVersion}_setup
SetupIconFile=..\..\src\TaskNova.App\Assets\tasknova.ico
UninstallDisplayIcon={app}\tasknova.exe
UninstallDisplayName=TaskNova {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=images\wizard-100.bmp,images\wizard-125.bmp,images\wizard-150.bmp,images\wizard-175.bmp,images\wizard-200.bmp,images\wizard-225.bmp,images\wizard-250.bmp
WizardSmallImageFile=images\wizard-small-100.bmp,images\wizard-small-125.bmp,images\wizard-small-150.bmp,images\wizard-small-175.bmp,images\wizard-small-200.bmp,images\wizard-small-225.bmp,images\wizard-small-250.bmp
WizardImageStretch=no
; Windows 10 1809 / Server 2019 (build 17763) or later, 64-bit only
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=force
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.GroupExtras=Additional options:
de.GroupExtras=Weitere Optionen:
en.TaskDesktopIcon=Create a desktop shortcut
de.TaskDesktopIcon=Desktop-Verknüpfung erstellen
en.TaskTray=Start TaskNova in the system tray at sign-in (scheduled task for this account)
de.TaskTray=TaskNova bei der Anmeldung im Infobereich starten (geplante Aufgabe für dieses Konto)
en.TaskContextMenu=Add "TaskNova" to the desktop context menu (this account)
de.TaskContextMenu="TaskNova" zum Desktop-Kontextmenü hinzufügen (dieses Konto)
en.PurgeQuestion=Also delete TaskNova data (settings and app history)?
de.PurgeQuestion=Auch TaskNova-Daten löschen (Einstellungen und App-Verlauf)?

[Tasks]
Name: "tray"; Description: "{cm:TaskTray}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:TaskContextMenu}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "tasknova.portable,\data\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TaskNova"; Filename: "{app}\tasknova.exe"
Name: "{autodesktop}\TaskNova"; Filename: "{app}\tasknova.exe"; Tasks: desktopicon

[Run]
; Tray at sign-in: elevated apps are skipped in the Run key, so TaskNova registers a scheduled task.
Filename: "{app}\tasknova.exe"; Parameters: "--register-autostart"; Flags: runhidden waituntilterminated; Tasks: tray
Filename: "{app}\tasknova.exe"; Parameters: "--register-context-menu"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: contextmenu
Filename: "{app}\tasknova.exe"; Description: "{cm:LaunchProgram,TaskNova}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "{app}\tasknova.exe"; Parameters: "--unregister-autostart-all"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAutostart"
Filename: "{app}\tasknova.exe"; Parameters: "--unregister-integration"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveIntegration"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\data"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM tasknova.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) and
     (MsgBox(CustomMessage('PurgeQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
  begin
    DelTree(ExpandConstant('{userappdata}\TaskNova'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\TaskNova'), True, True, True);
  end;
end;
