#define MyAppName "GrevConnect"
#define MyAppVersion "1.2.2"
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
OutputBaseFilename=GrevConnect-1.2.2-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=GrevConnect 1.2.2
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
  OfficialStamp: TNewStaticText;

procedure InitializeWizard;
begin
  BritishJokesPage := CreateCustomPage(
    wpInstalling,
    'GREVCONNECT FINAL CONNECTIVITY COMPLIANCE NOTICE',
    'Form GC-42B · Department of Remote Connectivity · definitely not a real department');

  OfficialStamp := TNewStaticText.Create(BritishJokesPage);
  OfficialStamp.Parent := BritishJokesPage.Surface;
  OfficialStamp.Left := ScaleX(18);
  OfficialStamp.Top := ScaleY(12);
  OfficialStamp.Width := BritishJokesPage.SurfaceWidth - ScaleX(36);
  OfficialStamp.Height := ScaleY(34);
  OfficialStamp.AutoSize := False;
  OfficialStamp.WordWrap := True;
  OfficialStamp.Font.Style := [fsBold];
  OfficialStamp.Font.Size := 10;
  OfficialStamp.Caption :=
    'OFFICIAL-LOOKING NOTICE · NOT OFFICIAL · NO GOVERNMENT BODY HAS APPROVED THIS NONSENSE';

  JokeText := TNewStaticText.Create(BritishJokesPage);
  JokeText.Parent := BritishJokesPage.Surface;
  JokeText.Left := ScaleX(18);
  JokeText.Top := ScaleY(55);
  JokeText.Width := BritishJokesPage.SurfaceWidth - ScaleX(36);
  JokeText.Height := ScaleY(275);
  JokeText.AutoSize := False;
  JokeText.WordWrap := True;
  JokeText.Font.Size := 9;
  JokeText.Caption :=
    'COMPLIANCE STATUS: PASS, somehow.' + #13#10 + #13#10 +
    'Why did the British computer cross the road?' + #13#10 +
    'It didn''t. It queued politely until the road apologised first.' + #13#10 + #13#10 +
    'GrevConnect tried to make tea during installation, but Windows said the kettle was an untrusted publisher.' + #13#10 + #13#10 +
    'Network test result: the router was asked to remain calm and think of England.' + #13#10 + #13#10 +
    'Security audit: one suspicious biscuit was detected near port 5900. It has been dunked and the incident is closed.' + #13#10 + #13#10 +
    'Final operational requirement: if anything stops working, tut loudly, say ''bloody computers'', then try turning it off and on again.' + #13#10 + #13#10 +
    'Authorised by: absolutely nobody. Filing status: placed carefully in a drawer and forgotten about.';
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
