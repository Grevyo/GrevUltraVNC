param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'friend-app'
$downloadDir = Join-Path $dist 'friend-downloads'
$uvncExtract = Join-Path $downloadDir 'UltraVNC-1824'
$uvncTarget = Join-Path $publish 'UltraVNC'
$licenses = Join-Path $publish 'Licenses'

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $downloadDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $downloadDir, $licenses -Force | Out-Null

Write-Host 'Publishing GrevUltraVNC 1.1.7 self-contained win-x64...'
dotnet publish (Join-Path $root 'src\GrevUltraVNC\GrevUltraVNC.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publish `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Controller publish failed.' }

$uvncUrl = 'https://uvnc.eu/download/1800/UltraVNC_1824.zip'
$uvncZip = Join-Path $downloadDir 'UltraVNC_1824.zip'
$expectedUvncSha256 = '8AF948089626008F02EDD1254AFC15C814E454EC5FC9E3EAA860356F19D4F113'

Write-Host 'Downloading official UltraVNC 1.8.2.4 portable binaries...'
Invoke-WebRequest -Uri $uvncUrl -OutFile $uvncZip -UseBasicParsing
$actualUvncSha256 = (Get-FileHash -Path $uvncZip -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actualUvncSha256 -ne $expectedUvncSha256) {
    throw "UltraVNC ZIP hash mismatch. Expected $expectedUvncSha256 but got $actualUvncSha256. Refusing to package it."
}

Expand-Archive -Path $uvncZip -DestinationPath $uvncExtract -Force
$viewerCandidates = @(Get-ChildItem -Path $uvncExtract -Filter 'vncviewer.exe' -File -Recurse)
if ($viewerCandidates.Count -eq 0) {
    throw 'UltraVNC archive did not contain vncviewer.exe.'
}

$viewer = $viewerCandidates |
    Where-Object { $_.FullName -match '(?i)[\\/]x64[\\/]' } |
    Sort-Object { $_.FullName.Length } |
    Select-Object -First 1
if (-not $viewer) {
    $viewer = $viewerCandidates | Sort-Object Length -Descending | Select-Object -First 1
}

New-Item -ItemType Directory -Path $uvncTarget -Force | Out-Null
$bundledViewer = Join-Path $uvncTarget 'vncviewer.exe'
Copy-Item -Path $viewer.FullName -Destination $bundledViewer -Force
if (-not (Test-Path $bundledViewer)) {
    throw 'Bundled UltraVNC viewer was not copied to the expected location.'
}

Write-Host 'Smoke testing the bundled UltraVNC viewer...'
$viewerProcess = Start-Process -FilePath $bundledViewer -PassThru
Start-Sleep -Seconds 3
if ($viewerProcess.HasExited) {
    throw "Bundled UltraVNC viewer exited during startup smoke test with exit code $($viewerProcess.ExitCode)."
}
Stop-Process -Id $viewerProcess.Id -Force
Remove-Item (Join-Path $uvncTarget 'options.vnc') -Force -ErrorAction SilentlyContinue

$uvncLicenseUrl = 'https://raw.githubusercontent.com/ultravnc/UltraVNC/b1f54118e74124407705b342f4cc2269c74e8ca0/LICENSE'
Invoke-WebRequest -Uri $uvncLicenseUrl -OutFile (Join-Path $licenses 'UltraVNC-LICENSE.txt') -UseBasicParsing
Copy-Item (Join-Path $root 'installer\THIRD-PARTY-NOTICES.txt') (Join-Path $publish 'THIRD-PARTY-NOTICES.txt') -Force

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it before building the friend installer.'
}

# 1.1.7 keeps the 1.1.6 joke-installer template intact, then stamps this release and adds
# two deliberately pointless wizard pages. Nothing here changes the real installation actions.
$issSource = Join-Path $root 'installer\GrevUltraVNC-Friend.iss'
$iss = Join-Path $dist 'GrevUltraVNC-Friend-1.1.7.iss'
$issText = Get-Content -Path $issSource -Raw
$nl = [Environment]::NewLine

$issText = $issText.Replace('1.1.6', '1.1.7')

# Keep the joke captions, but make every navigation button short enough to actually fit.
$issText = $issText.Replace("WizardForm.BackButton.Caption := '< &Undo that nonsense';", "WizardForm.BackButton.Caption := '< &Regret';")
$issText = $issText.Replace("WizardForm.CancelButton.Caption := '&Panic and leave';", "WizardForm.CancelButton.Caption := '&Chicken out';")
$issText = $issText.Replace("WizardForm.DirBrowseButton.Caption := '&Pick another hole...';", "WizardForm.DirBrowseButton.Caption := '&Other hole...';")
$issText = $issText.Replace("WizardForm.NextButton.Caption := '&Do the stupid thing >';", "WizardForm.NextButton.Caption := '&Go on >';")
$issText = $issText.Replace("WizardForm.NextButton.Caption := '&Shove it in there >';", "WizardForm.NextButton.Caption := '&Shove it >';")
$issText = $issText.Replace("WizardForm.NextButton.Caption := '&Whatever, carry on >';", "WizardForm.NextButton.Caption := '&Carry on >';")
$issText = $issText.Replace("WizardForm.NextButton.Caption := '&Install the bloody thing';", "WizardForm.NextButton.Caption := '&Install it';")
$issText = $issText.Replace("WizardForm.CancelButton.Caption := '&Abort this masterpiece';", "WizardForm.CancelButton.Caption := '&Abort';")
$issText = $issText.Replace("WizardForm.NextButton.Caption := '&Fuck off, we are done';", "WizardForm.NextButton.Caption := '&Piss off';")

# Add references for the two useless pages.
$issText = $issText.Replace(
    '  LastInstallQuoteBucket: Integer;',
    '  LastInstallQuoteBucket: Integer;' + $nl +
    '  BritishPage: TWizardPage;' + $nl +
    '  BiscuitPage: TInputOptionWizardPage;')

$pointlessPages = @'
procedure CreatePointlessPages;
var
  FlagBase, FlagHorizontal, FlagVertical: TPanel;
  BritishBlurb, BritishFooter: TNewStaticText;
begin
  BritishPage := CreateCustomPage(
    wpWelcome,
    'Mandatory Englishness checkpoint',
    'No technical reason whatsoever. This page achieves absolutely fuck all.');

  { A proper St George cross built from controls so the EXE needs no extra image asset. }
  FlagBase := TPanel.Create(BritishPage);
  FlagBase.Parent := BritishPage.Surface;
  FlagBase.Width := ScaleX(300);
  FlagBase.Height := ScaleY(160);
  FlagBase.Left := (BritishPage.SurfaceWidth - FlagBase.Width) div 2;
  FlagBase.Top := ScaleY(14);
  FlagBase.BevelOuter := bvNone;
  FlagBase.BorderStyle := bsSingle;
  FlagBase.Color := clWhite;
  FlagBase.Caption := '';

  FlagHorizontal := TPanel.Create(BritishPage);
  FlagHorizontal.Parent := FlagBase;
  FlagHorizontal.Left := 0;
  FlagHorizontal.Top := (FlagBase.Height - ScaleY(28)) div 2;
  FlagHorizontal.Width := FlagBase.Width;
  FlagHorizontal.Height := ScaleY(28);
  FlagHorizontal.BevelOuter := bvNone;
  FlagHorizontal.Color := clRed;
  FlagHorizontal.Caption := '';

  FlagVertical := TPanel.Create(BritishPage);
  FlagVertical.Parent := FlagBase;
  FlagVertical.Left := (FlagBase.Width - ScaleX(28)) div 2;
  FlagVertical.Top := 0;
  FlagVertical.Width := ScaleX(28);
  FlagVertical.Height := FlagBase.Height;
  FlagVertical.BevelOuter := bvNone;
  FlagVertical.Color := clRed;
  FlagVertical.Caption := '';

  BritishBlurb := TNewStaticText.Create(BritishPage);
  BritishBlurb.Parent := BritishPage.Surface;
  BritishBlurb.Left := ScaleX(18);
  BritishBlurb.Top := ScaleY(192);
  BritishBlurb.Width := BritishPage.SurfaceWidth - ScaleX(36);
  BritishBlurb.Height := ScaleY(58);
  BritishBlurb.AutoSize := False;
  BritishBlurb.WordWrap := True;
  BritishBlurb.Font.Style := [fsBold];
  BritishBlurb.Caption :=
    'ENGLAND HAS ENTERED THE INSTALLER.' + #13#10 +
    'Please complain about the weather, form an orderly queue, tut at something and apologise to a chair before continuing.';

  BritishFooter := TNewStaticText.Create(BritishPage);
  BritishFooter.Parent := BritishPage.Surface;
  BritishFooter.Left := ScaleX(18);
  BritishFooter.Top := ScaleY(258);
  BritishFooter.Width := BritishPage.SurfaceWidth - ScaleX(36);
  BritishFooter.Height := ScaleY(55);
  BritishFooter.AutoSize := False;
  BritishFooter.WordWrap := True;
  BritishFooter.Caption :=
    'Tea status: assumed. Monarchy status: none of our business. Packet queue: immaculate.' + #13#10 +
    'God save the ping. This page changes literally nothing.';

  BiscuitPage := CreateInputOptionPage(
    BritishPage.ID,
    'Critical biscuit compatibility test',
    'This setting also changes absolutely fuck all.',
    'Choose the biscuit you would trust near a cup of tea. Your answer is immediately ignored.',
    True,
    False);
  BiscuitPage.Add('&Custard Cream - structurally dependable');
  BiscuitPage.Add('&Digestive - boring but reliable');
  BiscuitPage.Add('&Hobnob - built like a fucking patio slab');
  BiscuitPage.Add('&Rich Tea - one dunk away from structural collapse');
  BiscuitPage.Add('&No biscuit - suspicious behaviour');
  BiscuitPage.SelectedValueIndex := 2;
end;

'@

$issText = $issText.Replace('procedure InitializeWizard;', $pointlessPages + 'procedure InitializeWizard;')
$issText = $issText.Replace('  LastInstallQuoteBucket := -1;', '  LastInstallQuoteBucket := -1;' + $nl + '  CreatePointlessPages;')

$curPageRegex = [regex]::new('begin\r?\n  SetStupidButtons;\r?\n\r?\n  case CurPageID of')
$curPageReplacement = @"
begin
  SetStupidButtons;

  if (BritishPage <> nil) and (CurPageID = BritishPage.ID) then
  begin
    WizardForm.NextButton.Caption := '&British >';
    exit;
  end;

  if (BiscuitPage <> nil) and (CurPageID = BiscuitPage.ID) then
  begin
    WizardForm.NextButton.Caption := '&Biscuits >';
    exit;
  end;

  case CurPageID of
"@
$issText = $curPageRegex.Replace($issText, $curPageReplacement, 1)

Set-Content -Path $iss -Value $issText -Encoding UTF8

Write-Host 'Building single EXE installer...'
& $iscc "/DSourceDir=$publish" "/DOutputDir=$dist" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$installer = Join-Path $dist 'GrevUltraVNC-1.1.7-Friend-Setup.exe'
if (-not (Test-Path $installer)) {
    throw 'Expected installer EXE was not created.'
}

$installerHash = (Get-FileHash -Path $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Friend installer: $installer"
Write-Host "SHA256: $installerHash"
