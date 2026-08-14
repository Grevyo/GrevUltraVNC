#define MyAppName "GrevUltraVNC"
#define MyAppVersion "1.1.4"
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
OutputBaseFilename=GrevUltraVNC-1.1.4-Friend-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayName=GrevUltraVNC 1.1.4 Friend Test
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
  case Index mod 160 of
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
    64: Result := 'Rearranging the furniture was considered and rejected on legal advice.';
    65: Result := 'Measuring the desk in packets per second.';
    66: Result := 'Assembling a mouse from locally sourced coordinates.';
    67: Result := 'Checking whether the monitor is plugged into reality.';
    68: Result := 'Lubricating the scroll bars with premium-grade nonsense.';
    69: Result := 'Installing one invisible cable. Please do not trip over it.';
    70: Result := 'Sending a technician into the registry with a torch and a sandwich.';
    71: Result := 'Making sure the cursor has completed its right-edge driving test.';
    72: Result := 'Folding the remote desktop neatly before putting it away.';
    73: Result := 'Replacing several ordinary bytes with more confident bytes.';
    74: Result := 'Checking the packet cupboard. Plenty of packets.';
    75: Result := 'Giving Windows one last chance to behave voluntarily.';
    76: Result := 'Installing a backup plan for the backup plan.';
    77: Result := 'Warming up the emergency Ctrl Alt Delete.';
    78: Result := 'Dusting the second screen. It was mostly virtual dust.';
    79: Result := 'Asking the router where it was on the night of the outage.';
    80: Result := 'Placing the files alphabetically. Windows will ruin this later.';
    81: Result := 'Training the eraser to distinguish whiteboard from actual monitor.';
    82: Result := 'Teaching Undo that consequences are sometimes optional.';
    83: Result := 'Preparing thirteen different ways to point at the same thing.';
    84: Result := 'Adding cursors with absolutely unnecessary personality.';
    85: Result := 'Checking the Grev squiggle for structural integrity.';
    86: Result := 'ChatGPT made its own squiggle. Nobody asked it to behave.';
    87: Result := 'Installing a crosshair for extremely serious spreadsheet work.';
    88: Result := 'Polishing the ring cursor until it becomes somebody else problem.';
    89: Result := 'Sharpening the diamond cursor. Safety goggles recommended.';
    90: Result := 'Pixelating a pointer by hand for authenticity.';
    91: Result := 'Giving the classic arrow a respectful nod.';
    92: Result := 'Making the name tag move instead of bullying the mouse away from the edge.';
    93: Result := 'Checking far right. Still there. Excellent.';
    94: Result := 'Checking far left because now we are suspicious.';
    95: Result := 'Checking the top edge. Ceiling appears operational.';
    96: Result := 'Checking the bottom edge. Floor remains installed.';
    97: Result := 'Counting pixels individually. We lost count at pixel.';
    98: Result := 'Briefly considering a progress circle. Absolutely not.';
    99: Result := 'Installing no toolbars, coupons, weather apps or browser extensions.';
    100: Result := 'The printer has been informed. It declined to comment.';
    101: Result := 'Trying to remember why printers need magenta to print black.';
    102: Result := 'Ignoring the printer. Healthy boundaries are important.';
    103: Result := 'Running a highly scientific vibe check on the network.';
    104: Result := 'Vibe check inconclusive. Installing anyway.';
    105: Result := 'Checking whether localhost is at home.';
    106: Result := 'Localhost confirmed local.';
    107: Result := 'Requesting planning permission for a temporary socket.';
    108: Result := 'Socket approved subject to tiny imaginary conditions.';
    109: Result := 'Putting a little hat on TCP and calling it secure enough for this joke.';
    110: Result := 'Untangling a cable that does not physically exist.';
    111: Result := 'Performing preventative maintenance on the progress bar ego.';
    112: Result := 'Removing crumbs from the protocol.';
    113: Result := 'Sweeping old packets into a neat pile.';
    114: Result := 'Asking packet loss to please stop losing the packets.';
    115: Result := 'Checking if the firewall has calmed down yet.';
    116: Result := 'Firewall still staring. Avoid sudden movements.';
    117: Result := 'Registering GrevUltraVNC with the Department of Computer Stuff.';
    118: Result := 'Department of Computer Stuff says this looks computer enough.';
    119: Result := 'Installing a remote desktop without disturbing the neighbours.';
    120: Result := 'Turning down the volume on several loud DLLs.';
    121: Result := 'Putting the EXE somewhere it can think about what it has done.';
    122: Result := 'Checking that the shortcut knows the way home.';
    123: Result := 'Shortcut has been issued a tiny map.';
    124: Result := 'Applying two coats of LAN and one coat of Grev Connect.';
    125: Result := 'Waiting for the first coat of LAN to dry.';
    126: Result := 'Do not touch it. The LAN is still tacky.';
    127: Result := 'LAN is dry. Carry on.';
    128: Result := 'Inspecting the virtual monitor for fingerprints.';
    129: Result := 'There are no fingerprints because it is virtual. Good result.';
    130: Result := 'Preparing a second screen in case one screen becomes lonely.';
    131: Result := 'Adding enough buttons to qualify as a cockpit.';
    132: Result := 'Labelling the dangerous buttons so curiosity can find them faster.';
    133: Result := 'Checking that Restart still means Restart. Surprisingly, yes.';
    134: Result := 'Checking that Shut down remains deeply committed to the bit.';
    135: Result := 'Packing one fresh Ctrl Shift Escape for emergencies.';
    136: Result := 'Packing a spare Windows key beside the sandwiches.';
    137: Result := 'Testing Alt Tab on two imaginary windows.';
    138: Result := 'Installing a tasteful amount of cyan.';
    139: Result := 'Adding more cyan because tasteful was never the objective.';
    140: Result := 'Negotiating cursor custody with the collaboration service.';
    141: Result := 'The collaboration service would like weekends off.';
    142: Result := 'Synchronising whiteboard crimes across the network.';
    143: Result := 'Teaching the eraser that deleting the monitor itself is frowned upon.';
    144: Result := 'Giving Undo forty chances to reconsider recent decisions.';
    145: Result := 'Making sure Clear All still means absolutely all of it.';
    146: Result := 'Hiding another joke in the installer. You found it.';
    147: Result := 'This message replaced a useful status update. Worth it.';
    148: Result := 'Checking disk space by looking concerned at the drive.';
    149: Result := 'Drive has enough room and appreciates the concern.';
    150: Result := 'Installing at the speed permitted by local bureaucracy.';
    151: Result := 'Stamping form TCP-5900 in triplicate.';
    152: Result := 'Misplacing form TCP-5900 and printing another.';
    153: Result := 'Finding the original form immediately afterwards.';
    154: Result := 'Putting both forms in the same drawer for future confusion.';
    155: Result := 'Final furniture inspection in progress.';
    156: Result := 'Sofa unchanged. Chair unchanged. Router slightly nervous.';
    157: Result := 'Returning the imaginary screwdriver to its imaginary drawer.';
    158: Result := 'Tidying up the evidence and pretending this was professional.';
    159: Result := 'Installation complete. Please release the network hamster.';
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
          'This installs GrevUltraVNC 1.1.4 and the UltraVNC viewer it needs.' + #13#10 + #13#10 +
          GrevQuote(1) + #13#10 + #13#10 + GrevQuote(83);
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
          'Press Install when emotionally prepared.' + #13#10 + #13#10 + GrevQuote(64);
      end;

    wpInstalling:
      begin
        WizardForm.StatusLabel.Caption := GrevQuote(10);
      end;

    wpFinished:
      begin
        WizardForm.FinishedLabel.Caption :=
          GrevQuote(159) + #13#10 + #13#10 +
          'Launch GrevUltraVNC, paste the GC- ID Grev gave you, and off you go.' + #13#10 + #13#10 +
          GrevQuote(158);
      end;
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  QuoteBucket: Integer;
  QuoteIndex: Integer;
  BucketSize: Integer;
begin
  if MaxProgress <= 0 then
    exit;

  if MaxProgress > 48 then
  begin
    BucketSize := MaxProgress div 48;
    if BucketSize < 1 then
      BucketSize := 1;
    QuoteBucket := CurProgress div BucketSize;
  end
  else
    QuoteBucket := CurProgress;

  if QuoteBucket > 48 then
    QuoteBucket := 48;

  if QuoteBucket <> LastInstallQuoteBucket then
  begin
    QuoteIndex := 10 + ((QuoteBucket * 149) div 48);
    if QuoteIndex > 159 then
      QuoteIndex := 159;
    WizardForm.StatusLabel.Caption := GrevQuote(QuoteIndex);
    LastInstallQuoteBucket := QuoteBucket;
  end;
end;