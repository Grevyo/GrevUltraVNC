using System.Windows;
using GrevUltraVNC.Models;
using WinForms = System.Windows.Forms;

namespace GrevUltraVNC.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Window _window;
    private readonly Func<IEnumerable<Machine>> _getFavorites;
    private readonly Func<IEnumerable<Machine>> _getAll;
    private readonly Action<Machine> _connect;
    private readonly Func<Task> _refresh;
    private readonly Action _openAbout;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon? _brandIcon;

    public TrayIconService(
        Window window,
        Func<IEnumerable<Machine>> getFavorites,
        Func<IEnumerable<Machine>> getAll,
        Action<Machine> connect,
        Func<Task> refresh,
        Action openAbout)
    {
        _window = window;
        _getFavorites = getFavorites;
        _getAll = getAll;
        _connect = connect;
        _refresh = refresh;
        _openAbout = openAbout;
        _brandIcon = BrandAssets.CreateDrawingIcon();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = ProductBranding.ProductName,
            Icon = _brandIcon ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
        _menu.Opening += (_, _) => RebuildMenu();
    }

    public void ShowDashboard()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        var all = _getAll().ToList();
        UpdateTooltip(all);

        var summary = new WinForms.ToolStripMenuItem(DescribeFleet(all)) { Enabled = false };
        _menu.Items.Add(summary);
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var open = new WinForms.ToolStripMenuItem("Open GrevConnect");
        open.Click += (_, _) => ShowDashboard();
        _menu.Items.Add(open);

        var refresh = new WinForms.ToolStripMenuItem("Refresh all machines");
        refresh.Click += (_, _) => _ = _refresh();
        _menu.Items.Add(refresh);

        var favorites = _getFavorites().OrderBy(machine => machine.Name).ToList();
        if (favorites.Count > 0)
        {
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            _menu.Items.Add(new WinForms.ToolStripMenuItem("Favourite machines") { Enabled = false });

            foreach (var machine in favorites)
            {
                var item = new WinForms.ToolStripMenuItem($"{StatusGlyph(machine)}  {machine.Name}")
                {
                    ToolTipText = $"{machine.RouteBadge} · {machine.RouteDetail}"
                };
                item.Click += (_, _) =>
                {
                    ShowDashboard();
                    _connect(machine);
                };
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        var about = new WinForms.ToolStripMenuItem("About GrevConnect");
        about.Click += (_, _) =>
        {
            ShowDashboard();
            _openAbout();
        };
        _menu.Items.Add(about);

        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Application.Current.Shutdown();
        _menu.Items.Add(exit);
    }

    private void UpdateTooltip(IReadOnlyCollection<Machine> machines)
    {
        var text = machines.Count == 0
            ? ProductBranding.ProductName
            : $"{ProductBranding.ProductName} · {DescribeFleet(machines)}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static string DescribeFleet(IReadOnlyCollection<Machine> machines)
    {
        if (machines.Count == 0) return "No machines configured";

        var online = machines.Count(machine => machine.Status is MachineStatus.Online or MachineStatus.VncUnavailable);
        var offline = machines.Count(machine => machine.Status == MachineStatus.Offline);
        return $"{online} online, {offline} offline";
    }

    private static string StatusGlyph(Machine machine) => machine.Status switch
    {
        MachineStatus.Online => "●",
        MachineStatus.VncUnavailable => "◐",
        MachineStatus.Offline => "○",
        _ => "·"
    };

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _brandIcon?.Dispose();
    }
}
