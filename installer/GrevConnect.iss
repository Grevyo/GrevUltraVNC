#define MyAppName "GrevConnect"
#define MyAppVersion "1.2.1"
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
OutputBaseFilename=GrevConnect-1.2.1-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=GrevConnect 1.2.1
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
  BritishJokesPage := CreateCustomPage(
    wpInstalling,
    'Installation complete. Unfortunately, Britain remains.',
    'Two final quality-control checks from the Department of Pointless Computer Things.');

  JokeText := TNewStaticText.Create(BritishJokesPage);
  JokeText.Parent := BritishJokesPage.Surface;
  JokeText.Left := ScaleX(18);
  JokeText.Top := ScaleY(35);
  JokeText.Width := BritishJokesPage.SurfaceWidth - ScaleX(36);
  JokeText.Height := ScaleY(220);
  JokeText.AutoSize := False;
  JokeText.WordWrap := True;
  JokeText.Font.Size := 11;
  JokeText.Caption :=
    'Why did the British computer cross the road?' + #13#10 +
    'It didn''t. It queued politely until the road apologised first.' + #13#10 + #13#10 +
    'GrevConnect tried to make tea during installation, but Windows said the kettle was an untrusted publisher.' + #13#10 + #13#10 +
    'Right. That is enough culture. Click Next.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (BritishJokesPage <> nil) and (CurPageID = BritishJokesPage.ID) then
    WizardForm.NextButton.Caption := '&Enough. Next >';

  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'Thank you for trying GrevConnect';
    WizardForm.FinishedLabel.Caption :=
      'Powered by UltraVNC' + #13#10 +
      'Thought up by Grev' + #13#10 +
      'Created by ChatGPT' + #13#10 +
      'Assisted by Claude' + #13#10 + #13#10 +
      'GrevConnect is ready to use.';
  end;
end;
