using System.Windows;
using GrevUltraVNC.Models;
using WinForms = System.Windows.Forms;

namespace GrevUltraVNC.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Window _window;
    private readonly Func<IEnumerable<Machine>> _getFavorites;
    private readonly Action<Machine> _connect;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu = new();
    private readonly System.Drawing.Icon? _brandIcon;

    public TrayIconService(Window window, Func<IEnumerable<Machine>> getFavorites, Action<Machine> connect)
    {
        _window = window;
        _getFavorites = getFavorites;
        _connect = connect;
        _brandIcon = BrandAssets.CreateDrawingIcon();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "GrevUltraVNC",
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

        var open = new WinForms.ToolStripMenuItem("Open GrevUltraVNC");
        open.Click += (_, _) => ShowDashboard();
        _menu.Items.Add(open);

        var favorites = _getFavorites().OrderBy(x => x.Name).ToList();
        if (favorites.Count > 0)
        {
            _menu.Items.Add(new WinForms.ToolStripSeparator());
            var heading = new WinForms.ToolStripMenuItem("Favourite machines") { Enabled = false };
            _menu.Items.Add(heading);

            foreach (var machine in favorites)
            {
                var item = new WinForms.ToolStripMenuItem($"Connect · {machine.Name}");
                item.Click += (_, _) =>
                {
                    ShowDashboard();
                    _connect(machine);
                };
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Application.Current.Shutdown();
        _menu.Items.Add(exit);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _brandIcon?.Dispose();
    }
}
