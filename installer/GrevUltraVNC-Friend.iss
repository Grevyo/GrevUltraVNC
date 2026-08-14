#define MyAppName "GrevUltraVNC"
#define MyAppVersion "1.1.1"
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
OutputBaseFilename=GrevUltraVNC-1.1.1-Friend-Setup
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
var
  LastInstallQuoteBucket: Integer;

function GrevQuote(Index: Integer): String;
begin
  case Index mod 64 of
    0: Result := 'It promises not to rearrange the furniture. Probably.';
    1: Result := 'Installing serious remote access software with deeply unserious supervision.';
    2: Result := 'No computers were consulted before beginning this installation.';
    3: Result := 'Preparing several megabytes of buttons Grev will definitely press responsibly.';
    4: Result := 'Checking whether the network hamster has remembered its little hat.';
    5: Result := 'Asking Windows nicely. This is rarely effective, but manners cost nothing.';
    6: Result := 'Placing files in folders. Revolutionary technology.';
    7: Result := 'Do not unplug the computer unless you enjoy creating side quests.';
    8: Result := 'Calibrating the remote-control doohickey.';
    9: Result := 'Convincing UltraVNC that this is all completely normal.';
    10: Result := 'Unpacking Grev bits. Some assembly emotionally required.';
    11: Result := 'Reticulating remote desktops.';
    12: Result := 'Teaching the mouse where the other computer lives.';
    13: Result := 'Allocating one complimentary virtual screwdriver.';
    14: Result := 'Checking behind the sofa for missing packets.';
    15: Result := 'Installing enough networking to concern a nearby printer.';
    16: Result := 'Polishing the Connect button. Fingerprints are unacceptable.';
    17: Result := 'Negotiating with Windows Defender using only eye contact.';
    18: Result := 'Please wait while absolutely legitimate computer things happen.';
    19: Result := 'Adding a small amount of Grev to this computer.';
    20: Result := 'Verifying the machine still believes in itself.';
    21: Result := 'Giving the progress bar something to feel important about.';
    22: Result := 'Searching for the correct drawer to put the DLLs in.';
    23: Result := 'Tightening the pixels to manufacturer specification.';
    24: Result := 'Applying remote access with a medium-sized spoon.';
    25: Result := 'Reassuring the firewall that we know a bloke.';
    26: Result := 'Installing the bit that makes Grev say "why is that doing that?" later.';
    27: Result := 'Counting monitors. One, two, several. Good enough.';
    28: Result := 'Checking Zima for signs of intelligent life.';
    29: Result := 'Feeding the connection tunnel its approved daily packet allowance.';
    30: Result := 'Putting the Ultra in UltraVNC. The VNC was already there.';
    31: Result := 'Hiding technical debt under a tastefully placed rug.';
    32: Result := 'Making the cursor pointier.';
    33: Result := 'Removing one unnecessary loading message and replacing it with this one.';
    34: Result := 'Please admire the progress bar. It has trained for this moment.';
    35: Result := 'Converting electricity into remote desktop, somehow.';
    36: Result := 'Installing friendship over TCP.';
    37: Result := 'Making a folder and immediately becoming emotionally attached to it.';
    38: Result := 'Checking that port 5900 has packed its lunch.';
    39: Result := 'Checking that port 47820 has remembered the keys.';
    40: Result := 'Do not worry. The suspicious amount of networking is intentional.';
    41: Result := 'Applying industry-standard quantities of hope.';
    42: Result := 'Aligning the bytes so the labels face forwards.';
    43: Result := 'Waiting for Windows to finish thinking about something unrelated.';
    44: Result := 'Turning it off and on again pre-emptively.';
    45: Result := 'The installer has detected a keyboard. Bold of you.';
    46: Result := 'Generating absolutely no blockchain whatsoever.';
    47: Result := 'Downloading nothing from a man in a van.';
    48: Result := 'Checking the warranty. We have chosen not to discuss the warranty.';
    49: Result := 'Installing a small emergency button labelled "More".';
    50: Result := 'Preparing the highly advanced technology known as a shortcut.';
    51: Result := 'Giving every packet a motivational speech.';
    52: Result := 'Making sure the remote PC cannot smell fear.';
    53: Result := 'Compiling confidence. Linking optimism.';
    54: Result := 'Opening a tiny door in the computer and putting GrevUltraVNC inside.';
    55: Result := 'Checking for loose screws. Found user. Continuing anyway.';
    56: Result := 'Installing the bit your antivirus will stare at suspiciously.';
    57: Result := 'Preparing to blame DNS even when it is clearly not DNS.';
    58: Result := 'Reserving 12 percent of the install for dramatic tension.';
    59: Result := 'Everything is fine. This message has no legal authority.';
    60: Result := 'Applying the final coat of remote desktop varnish.';
    61: Result := 'Nearly done. The hamster is sweating but morale remains acceptable.';
    62: Result := 'Tidying up. The furniture remains largely where we found it.';
    63: Result := 'Against all odds, the computer appears to have survived.';
  end;
end;

procedure InitializeWizard;
begin
  LastInstallQuoteBucket := -1;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  case CurPageID of
    wpWelcome:
      begin
        WizardForm.WelcomeLabel2.Caption :=
          'This installs GrevUltraVNC 1.1.1 and the UltraVNC viewer it needs.' + #13#10 + #13#10 +
          GrevQuote(1) + #13#10 + #13#10 + GrevQuote(3);
      end;

    wpSelectDir:
      begin
        WizardForm.SelectDirLabel.Caption :=
          'Pick where GrevUltraVNC should live.' + #13#10 + #13#10 + GrevQuote(0);
      end;

    wpReady:
      begin
        WizardForm.ReadyLabel.Caption :=
          'Everything is ready. Zima is not installed by this setup, so make sure your Grev Connect network is connected before the first remote test.' + #13#10 + #13#10 +
          'Press Install when emotionally prepared.' + #13#10 + #13#10 + GrevQuote(7);
      end;

    wpInstalling:
      begin
        WizardForm.StatusLabel.Caption := GrevQuote(10);
      end;

    wpFinished:
      begin
        WizardForm.FinishedLabel.Caption :=
          GrevQuote(63) + #13#10 + #13#10 +
          'Launch GrevUltraVNC, paste the GC- ID Grev gave you, and off you go.' + #13#10 + #13#10 +
          GrevQuote(62);
      end;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  QuoteBucket: Integer;
  BucketSize: Integer;
begin
  if MaxProgress <= 0 then
    exit;

  if MaxProgress > 30 then
  begin
    BucketSize := MaxProgress div 30;
    if BucketSize < 1 then
      BucketSize := 1;
    QuoteBucket := CurProgress div BucketSize;
  end
  else
    QuoteBucket := CurProgress;

  if QuoteBucket > 30 then
    QuoteBucket := 30;

  if QuoteBucket <> LastInstallQuoteBucket then
  begin
    WizardForm.StatusLabel.Caption := GrevQuote(10 + QuoteBucket);
    LastInstallQuoteBucket := QuoteBucket;
  end;
end;
