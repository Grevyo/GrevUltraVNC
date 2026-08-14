#define MyAppName "GrevUltraVNC"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Grev"
#define MyAppExeName "GrevUltraVNC.exe"

#ifndef SourceDir
  #define SourceDir "..\dist\friend-app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{E0DF26ED-11F3-467B-A36D-02FC2C5A6C31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} Friend Test
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\GrevUltraVNC
DefaultGroupName=GrevUltraVNC
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=GrevUltraVNC-1.1.0-Friend-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=GrevUltraVNC 1.1 Friend Test
UninstallDisplayIcon={app}\GrevUltraVNC.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GrevUltraVNC"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GrevUltraVNC"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch GrevUltraVNC"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurPageChanged(CurPageID: Integer);
begin
  case CurPageID of
    wpWelcome:
      begin
        WizardForm.WelcomeLabel2.Caption :=
          'This installs GrevUltraVNC 1.1 and the UltraVNC viewer it needs.' + #13#10 + #13#10 +
          'Remote control, collaboration, and just enough buttons to look mildly dangerous.';
      end;

    wpSelectDir:
      begin
        WizardForm.SelectDirLabel.Caption :=
          'Pick where GrevUltraVNC should live. It promises not to rearrange the furniture.';
      end;

    wpReady:
      begin
        WizardForm.ReadyLabel.Caption :=
          'Everything is ready. Zima is not installed by this setup, so make sure your Grev Connect network is connected before the first remote test.' + #13#10 + #13#10 +
          'Press Install when emotionally prepared.';
      end;

    wpInstalling:
      begin
        WizardForm.StatusLabel.Caption := 'Unpacking the Grev bits and checking the network hamster...';
      end;

    wpFinished:
      begin
        WizardForm.FinishedLabel.Caption :=
          'Installed. Against all odds, the computer survived.' + #13#10 + #13#10 +
          'Launch GrevUltraVNC, paste the GC- ID Grev gave you, and off you go.';
      end;
  end;
end;
