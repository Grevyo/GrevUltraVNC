using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private readonly DispatcherTimer _adaptivePanelTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private const string PrepareVirtualDisplayDriverScript = """
$ErrorActionPreference = 'Stop'
$service = Get-CimInstance Win32_Service -Filter "Name='uvnc_service'"
if ($null -eq $service) { throw 'UltraVNC service uvnc_service was not found.' }
$raw = $service.PathName.Trim()
if ($raw.StartsWith('"')) { $exe = $raw.Split('"')[1] } else { $exe = ($raw -split '\s+')[0] }
if (-not (Test-Path -LiteralPath $exe)) { throw 'UltraVNC winvnc.exe could not be found from the service path.' }
$root = Split-Path -Parent $exe
$inf = Get-ChildItem -LiteralPath $root -Recurse -Filter 'UVncVirtualDisplay.inf' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $inf) { throw 'UltraVNC virtual-display driver files are not installed. Reinstall UltraVNC Server with the virtual monitor files included.' }
$cat = Get-ChildItem -LiteralPath $inf.DirectoryName -Filter '*.cat' -File -ErrorAction SilentlyContinue | Select-Object -First 1
$store = $null
$cert = $null
$added = $false
try {
    if ($null -ne $cat) { $cert = (Get-AuthenticodeSignature -FilePath $cat.FullName).SignerCertificate }
    if ($null -ne $cert) {
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher','LocalMachine')
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $existing = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $cert.Thumbprint, $false)
        if ($existing.Count -eq 0) { $store.Add($cert); $added = $true }
        $store.Close()
    }

    $arguments = "/add-driver `"$($inf.FullName)`" /install"
    $process = Start-Process -FilePath "$env:SystemRoot\System32\pnputil.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Windows could not install the UltraVNC virtual-display driver (pnputil exit $($process.ExitCode))." }
    Write-Output 'READY'
}
finally {
    if ($added -and $null -ne $cert) {
        try {
            if ($null -eq $store) { $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher','LocalMachine') }
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $store.Remove($cert)
            $store.Close()
        } catch { }
    }
}
""";

    private const string VerifyVirtualDisplayScript = """
$device = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -like '*UVncVirtualDisplay*' -or $_.PNPDeviceID -like '*UVncVirtualDisplay*'
} | Select-Object -First 1
if ($null -ne $device) { 'READY|' + $device.Name } else { 'MISSING' }
""";

    private void AdaptivePanel_ContentRendered(object? sender, EventArgs e)
    {
        _adaptivePanelTimer.Tick -= AdaptivePanelTimer_Tick;
        _adaptivePanelTimer.Tick += AdaptivePanelTimer_Tick;
        _adaptivePanelTimer.Start();
        Dispatcher.BeginInvoke(ApplyAdaptivePanelSizing, DispatcherPriority.Loaded);
    }

    private void AdaptivePanel_Closed(object? sender, EventArgs e) => _adaptivePanelTimer.Stop();

    private void AdaptivePanelTimer_Tick(object? sender, EventArgs e) => ApplyAdaptivePanelSizing();

    private void ApplyAdaptivePanelSizing()
    {
        if (!_vnc.TryGetViewerWindowHandle(_machine.Id, out var viewerHandle) || viewerHandle == IntPtr.Zero)
            return;

        var screen = System.Windows.Forms.Screen.FromHandle(viewerHandle);
        var dpi = VisualTreeHelper.GetDpi(this);
        var scale = dpi.DpiScaleY <= 0 ? 1d : dpi.DpiScaleY;
        var workHeight = screen.WorkingArea.Height / scale;

        const double desiredHeight = 900;
        const double chromeMargin = 14;
        const double minimumUsableHeight = 560;
        var availableHeight = Math.Max(minimumUsableHeight, workHeight - chromeMargin);
        var needsScroll = availableHeight + 1 < desiredHeight;

        // The panel is allowed to use the monitor height. Only smaller displays get scrolling.
        MinHeight = Math.Min(840, availableHeight);
        MaxHeight = Math.Max(MinHeight, Math.Min(1040, availableHeight));
        Height = Math.Min(desiredHeight, availableHeight);
        PanelScrollViewer.VerticalScrollBarVisibility = needsScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    private void ViewerScale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;

        try
        {
            if (string.Equals(tag, "fit", StringComparison.OrdinalIgnoreCase))
            {
                _vnc.FitToWindow(_machine.Id);
                ZoomStatusText.Text = "Fit";
                return;
            }

            if (!int.TryParse(tag, out var percent))
                return;

            _vnc.SetScale(_machine.Id, percent);
            ZoomStatusText.Text = $"{percent}%";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Viewer size", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void CreateScreen2_Click(object sender, RoutedEventArgs e)
    {
        if (_virtualDisplayStarting) return;

        if (_vnc.HasVirtualSession(_machine.Id))
        {
            try
            {
                _vnc.BringVirtualViewerToFront(_machine.Id);
                DisplayStatusText.Text = "Screen 1 physical · Screen 2 virtual";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Screen 2", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        _virtualDisplayStarting = true;
        VirtualDisplayButton.IsEnabled = false;
        VirtualDisplayButton.Content = "Preparing Screen 2…";
        DisplayStatusText.Text = "Preparing UltraVNC virtual monitor driver…";
        CollaborationStatusText.Text = "Preparing Screen 2";

        try
        {
            var preparation = await _collaborationClient.RunCommandAsync(
                _machine,
                "powershell",
                PrepareVirtualDisplayDriverScript,
                timeoutSeconds: 45);

            if (!preparation.Success || preparation.ExitCode != 0 ||
                !preparation.StandardOutput.Contains("READY", StringComparison.OrdinalIgnoreCase))
            {
                var detail = string.IsNullOrWhiteSpace(preparation.StandardError)
                    ? preparation.StandardOutput
                    : preparation.StandardError;
                throw new InvalidOperationException(
                    "The UltraVNC virtual-display driver could not be prepared on the host. " + detail.Trim());
            }

            DisplayStatusText.Text = "Creating Windows virtual monitor…";
            await _vnc.OpenVirtualDisplayAsync(_machine, _collaborationSettings);

            var virtualDeviceReady = await WaitForRemoteVirtualDisplayAsync();
            if (!virtualDeviceReady)
            {
                _vnc.CloseVirtualDisplay(_machine.Id);
                throw new InvalidOperationException(
                    "UltraVNC opened a second viewer, but Windows did not create a UVncVirtualDisplay device. " +
                    "Grev closed the duplicate viewer instead of pretending Screen 2 was ready.");
            }

            EnsureCursorOverlays();
            var localHasControl = string.Equals(
                _controlOwnerId,
                _collaborationSettings.ControllerId,
                StringComparison.OrdinalIgnoreCase);
            _vnc.SetViewOnly(_machine.Id, !localHasControl);
            UpdateDisplayState();
            DisplayStatusText.Text = "Screen 1 physical · Screen 2 virtual Windows display";
            CollaborationStatusText.Text = "Screen 2 ready";
        }
        catch (Exception ex)
        {
            try { _vnc.CloseVirtualDisplay(_machine.Id); } catch { }
            UpdateDisplayState();
            CollaborationStatusText.Text = "Screen 2 unavailable";
            MessageBox.Show(this, ex.Message, "Virtual Screen 2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _virtualDisplayStarting = false;
            UpdateDisplayState();
        }
    }

    private async Task<bool> WaitForRemoteVirtualDisplayAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var result = await _collaborationClient.RunCommandAsync(
                _machine,
                "powershell",
                VerifyVirtualDisplayScript,
                timeoutSeconds: 10);

            if (result.Success && result.StandardOutput.Contains("READY|", StringComparison.OrdinalIgnoreCase))
                return true;

            await Task.Delay(500);
        }

        return false;
    }
}
