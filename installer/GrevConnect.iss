#define MyAppName "GrevConnect"
#define MyAppVersion "1.2.3"
#define MyAppPublisher "Grev"
#define MyAppExeName "GrevConnect.exe"

#ifndef SourceDir
  #define SourceDir "..\dist\grevconnect-app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{E0DF26ED-11F3-467B-A36D-02FC2C5A6C31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=C:\GrevCo\GrevConnect
DefaultGroupName=GrevConnect
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=GrevConnect-1.2.3-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=GrevConnect 1.2.3
UninstallDisplayIcon={app}\GrevConnect.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GrevConnect"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GrevConnect"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch GrevConnect"; Flags: nowait postinstall skipifsilent

[Code]
var
  BritishJokesPage: TWizardPage;
  JokeText: TNewStaticText;

procedure InitializeWizard;
begin
  { Keep this page visually identical to a normal Inno Setup wizard page. }
  BritishJokesPage := CreateCustomPage(
    wpInstalling,
    'Completing GrevConnect Setup',
    'Setup is performing a few final checks before GrevConnect is ready to use.');

  JokeText := TNewStaticText.Create(BritishJokesPage);
  JokeText.Parent := BritishJokesPage.Surface;
  JokeText.Left := ScaleX(0);
  JokeText.Top := ScaleY(12);
  JokeText.Width := BritishJokesPage.SurfaceWidth;
  JokeText.Height := ScaleY(290);
  JokeText.AutoSize := False;
  JokeText.WordWrap := True;
  JokeText.Font.Size := 9;
  JokeText.Caption :=
    'Final setup checks:' + #13#10 + #13#10 +
    '  [OK] Remote connection components installed.' + #13#10 +
    '  [OK] UltraVNC components verified.' + #13#10 +
    '  [OK] Zima connection route acknowledged.' + #13#10 +
    '  [OK] Kettle status: probably fine.' + #13#10 + #13#10 +
    'Why did the British computer cross the road?' + #13#10 +
    'It didn''t. It queued politely until the road apologised first.' + #13#10 + #13#10 +
    'GrevConnect attempted to make tea during installation, but Windows reported the kettle as an untrusted publisher.' + #13#10 + #13#10 +
    'A suspicious biscuit was detected near port 5900. The biscuit has been dunked and no further action is required.' + #13#10 + #13#10 +
    'If GrevConnect ever stops responding, standard British troubleshooting procedure applies: tut quietly, blame the Wi-Fi, and try turning it off and on again.' + #13#10 + #13#10 +
    'Setup has completed these checks successfully. Click Next to continue.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (BritishJokesPage <> nil) and (CurPageID = BritishJokesPage.ID) then
    WizardForm.NextButton.Caption := '&Next >';

  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'Thank you for trying GrevConnect';
    WizardForm.FinishedLabel.Caption :=
      'Powered by UltraVNC' + #13#10 +
      'Connected by Zima' + #13#10 +
      'Thought up by Grev' + #13#10 +
      'Created by ChatGPT' + #13#10 +
      'Assisted by Claude' + #13#10 + #13#10 +
      'GrevConnect is ready to use.';
  end;
end;
