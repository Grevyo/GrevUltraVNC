using System.IO;
using System.Windows;
using System.Windows.Input;
using GrevUltraVNC.Contracts;
using GrevUltraVNC.Models;
using GrevUltraVNC.Services;
using Microsoft.Win32;

namespace GrevUltraVNC;

public partial class RemoteFileManagerWindow : Window
{
    private const int TransferChunkBytes = 256 * 1024;
    private const long MaxTransferBytes = 512L * 1024 * 1024;

    private readonly Machine _machine;
    private readonly GrevAgentClient _agent = new();
    private readonly AdminWorkspaceStorage _workspace = new();
    private string? _currentPath;
    private string? _clipboardPath;
    private bool _clipboardMove;
    private bool _busy;
    private IReadOnlyList<FileRow> _rows = [];

    public RemoteFileManagerWindow(Machine machine)
    {
        InitializeComponent();
        _machine = machine;
        MachineNameText.Text = machine.Name;
        Loaded += RemoteFileManagerWindow_Loaded;
        Closed += (_, _) => _agent.Dispose();
    }

    private async void RemoteFileManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var probe = await _agent.ProbeAsync(_machine);
        if (probe.State != GrevAgentState.Connected || !string.IsNullOrWhiteSpace(probe.Message))
        {
            StatusText.Text = probe.Message ?? "Grev Agent is not ready for file management.";
            MessageBox.Show(this,
                "Update Grev Agent on this machine before using native file management.",
                "Grev Agent update required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Close();
            return;
        }

        await BrowseAsync(null);
    }

    private async Task BrowseAsync(string? path)
    {
        if (_busy) return;
        SetBusy(true, path is null ? "Loading drives…" : $"Opening {path}…");
        try
        {
            var response = await _agent.RunFileRequestAsync(
                _machine,
                new AgentFileRequest(path is null ? "roots" : "list", Path: path));

            if (!response.Success)
                throw new IOException(response.Message);

            _currentPath = response.CurrentPath;
            _rows = (response.Entries ?? [])
                .Select(entry => new FileRow(
                    entry,
                    _currentPath is null ? "Drive" : entry.IsDirectory ? "Folder" : "File",
                    entry.Name,
                    entry.IsDirectory ? string.Empty : FormatBytes(entry.SizeBytes),
                    entry.LastWriteTimeUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? string.Empty,
                    entry.Detail))
                .ToArray();

            FilesList.ItemsSource = _rows;
            PathBox.Text = _currentPath ?? "This PC";
            UpButton.IsEnabled = _currentPath is not null;
            StatusText.Text = response.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Remote files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Drives_Click(object sender, RoutedEventArgs e) => await BrowseAsync(null);

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            await BrowseAsync(null);
            return;
        }

        var parent = Directory.GetParent(_currentPath)?.FullName;
        await BrowseAsync(parent);
    }

    private async void Go_Click(object sender, RoutedEventArgs e) => await GoToPathAsync();

    private async void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await GoToPathAsync();
    }

    private async Task GoToPathAsync()
    {
        var path = PathBox.Text.Trim();
        await BrowseAsync(string.IsNullOrWhiteSpace(path) || string.Equals(path, "This PC", StringComparison.OrdinalIgnoreCase) ? null : path);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await BrowseAsync(_currentPath);

    private async void FilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesList.SelectedItem is FileRow selected && selected.Source.IsDirectory)
            await BrowseAsync(selected.Source.FullPath);
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireFolder()) return;
        var prompt = new TextPromptWindow("New folder", "Enter a name for the new remote folder.") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        await RunMutationAsync(new AgentFileRequest("create-folder", Path: _currentPath, Name: prompt.Value), "Create folder", prompt.Value);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireFolder() || FilesList.SelectedItem is not FileRow selected)
        {
            StatusText.Text = "Select an item to rename.";
            return;
        }

        var prompt = new TextPromptWindow("Rename", "Enter the new remote name.", selected.Source.Name) { Owner = this };
        if (prompt.ShowDialog() != true || string.Equals(prompt.Value, selected.Source.Name, StringComparison.Ordinal)) return;
        await RunMutationAsync(new AgentFileRequest("rename", Path: selected.Source.FullPath, Name: prompt.Value), "Rename", selected.Source.Name);
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => SetRemoteClipboard(move: false);
    private void Cut_Click(object sender, RoutedEventArgs e) => SetRemoteClipboard(move: true);

    private void SetRemoteClipboard(bool move)
    {
        if (!RequireFolder() || FilesList.SelectedItem is not FileRow selected)
        {
            StatusText.Text = "Select a file or folder first.";
            return;
        }

        _clipboardPath = selected.Source.FullPath;
        _clipboardMove = move;
        PasteButton.IsEnabled = true;
        ClipboardStatusText.Text = $"{(move ? "Cut" : "Copy")}: {selected.Source.Name}";
        StatusText.Text = $"{selected.Source.Name} ready to {(move ? "move" : "copy")}.";
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireFolder() || string.IsNullOrWhiteSpace(_clipboardPath)) return;

        var operation = _clipboardMove ? "move" : "copy";
        var sourceName = Path.GetFileName(_clipboardPath.TrimEnd(Path.DirectorySeparatorChar));
        await RunMutationAsync(
            new AgentFileRequest(operation, Path: _clipboardPath, DestinationDirectory: _currentPath),
            _clipboardMove ? "Move" : "Copy",
            sourceName);

        if (_clipboardMove)
        {
            _clipboardPath = null;
            PasteButton.IsEnabled = false;
            ClipboardStatusText.Text = string.Empty;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireFolder() || FilesList.SelectedItem is not FileRow selected)
        {
            StatusText.Text = "Select an item to delete.";
            return;
        }

        var description = selected.Source.IsDirectory
            ? "This deletes the folder and everything inside it."
            : "This permanently deletes the remote file.";
        if (MessageBox.Show(this,
                $"Delete '{selected.Source.Name}' from {_machine.Name}?\n\n{description}",
                "Delete remote item",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunMutationAsync(new AgentFileRequest("delete", Path: selected.Source.FullPath), "Delete", selected.Source.Name);
    }

    private async Task RunMutationAsync(AgentFileRequest request, string action, string item)
    {
        if (_busy) return;
        SetBusy(true, $"{action}: {item}…");
        try
        {
            var response = await _agent.RunFileRequestAsync(_machine, request);
            if (!response.Success)
                throw new IOException(response.Message);

            StatusText.Text = response.Message;
            await LogActivityAsync(action, item, true);
            await BrowseCoreAsync(_currentPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync(action, ex.Message, false);
            MessageBox.Show(this, ex.Message, action, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task BrowseCoreAsync(string? path)
    {
        var response = await _agent.RunFileRequestAsync(
            _machine,
            new AgentFileRequest(path is null ? "roots" : "list", Path: path));
        if (!response.Success) throw new IOException(response.Message);

        _currentPath = response.CurrentPath;
        _rows = (response.Entries ?? [])
            .Select(entry => new FileRow(
                entry,
                _currentPath is null ? "Drive" : entry.IsDirectory ? "Folder" : "File",
                entry.Name,
                entry.IsDirectory ? string.Empty : FormatBytes(entry.SizeBytes),
                entry.LastWriteTimeUtc?.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? string.Empty,
                entry.Detail))
            .ToArray();
        FilesList.ItemsSource = _rows;
        PathBox.Text = _currentPath ?? "This PC";
        UpButton.IsEnabled = _currentPath is not null;
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireFolder() || _busy) return;

        var picker = new OpenFileDialog
        {
            Title = $"Upload to {_machine.Name} · {_currentPath}",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;

        var local = new FileInfo(picker.FileName);
        if (local.Length > MaxTransferBytes)
        {
            MessageBox.Show(this, "This first file-manager version limits a single upload to 512 MB.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = Path.Combine(_currentPath!, local.Name);
        var existing = _rows.FirstOrDefault(row => string.Equals(row.Source.Name, local.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && MessageBox.Show(this,
                $"'{local.Name}' already exists on {_machine.Name}. Replace it?",
                "Replace remote file",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, $"Uploading {local.Name}…");
        try
        {
            await using var stream = new FileStream(local.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, TransferChunkBytes, useAsync: true);
            var buffer = new byte[TransferChunkBytes];
            long offset = 0;
            var first = true;

            do
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                var data = Convert.ToBase64String(buffer, 0, read);
                var response = await _agent.RunFileRequestAsync(
                    _machine,
                    new AgentFileRequest("write-chunk", Path: target, Offset: offset, DataBase64: data, Truncate: first));
                if (!response.Success) throw new IOException(response.Message);

                offset = response.NextOffset;
                first = false;
                var percent = local.Length == 0 ? 100 : Math.Min(100, offset * 100.0 / local.Length);
                StatusText.Text = $"Uploading {local.Name} · {percent:0}%";
                if (read == 0) break;
            }
            while (offset < local.Length);

            StatusText.Text = $"Uploaded {local.Name}.";
            await LogActivityAsync("Upload", $"{local.Name} · {FormatBytes(local.Length)}", true);
            await BrowseCoreAsync(_currentPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await LogActivityAsync("Upload", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Upload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || FilesList.SelectedItem is not FileRow selected || selected.Source.IsDirectory)
        {
            StatusText.Text = "Select a file to download.";
            return;
        }

        if (selected.Source.SizeBytes > MaxTransferBytes)
        {
            MessageBox.Show(this, "This first file-manager version limits a single download to 512 MB.", "Download", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new SaveFileDialog
        {
            Title = $"Download from {_machine.Name}",
            FileName = selected.Source.Name,
            OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true) return;

        SetBusy(true, $"Downloading {selected.Source.Name}…");
        var localPath = picker.FileName;
        try
        {
            await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, TransferChunkBytes, useAsync: true);
            long offset = 0;
            while (true)
            {
                var response = await _agent.RunFileRequestAsync(
                    _machine,
                    new AgentFileRequest("read-chunk", Path: selected.Source.FullPath, Offset: offset));
                if (!response.Success) throw new IOException(response.Message);

                var data = Convert.FromBase64String(response.DataBase64 ?? string.Empty);
                if (data.Length > 0)
                    await output.WriteAsync(data.AsMemory());

                if (response.NextOffset < offset || (!response.Complete && response.NextOffset == offset))
                    throw new IOException("Remote transfer stopped making progress.");

                offset = response.NextOffset;
                var percent = selected.Source.SizeBytes == 0 ? 100 : Math.Min(100, offset * 100.0 / selected.Source.SizeBytes);
                StatusText.Text = $"Downloading {selected.Source.Name} · {percent:0}%";
                if (response.Complete) break;
            }

            await output.FlushAsync();
            StatusText.Text = $"Downloaded {selected.Source.Name}.";
            await LogActivityAsync("Download", $"{selected.Source.Name} · {FormatBytes(selected.Source.SizeBytes)}", true);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
            StatusText.Text = ex.Message;
            await LogActivityAsync("Download", ex.Message, false);
            MessageBox.Show(this, ex.Message, "Download", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool RequireFolder()
    {
        if (!string.IsNullOrWhiteSpace(_currentPath)) return true;
        StatusText.Text = "Open a drive or folder first.";
        return false;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        if (!string.IsNullOrWhiteSpace(message)) StatusText.Text = message;
    }

    private async Task LogActivityAsync(string action, string detail, bool success)
    {
        try
        {
            await _workspace.AppendActivityAsync(new ActivityEntry
            {
                MachineId = _machine.Id,
                MachineName = _machine.Name,
                TimestampUtc = DateTime.UtcNow,
                Category = "Files",
                Action = action,
                Detail = detail,
                Success = success
            });
        }
        catch
        {
            // File actions must not fail just because local activity logging failed.
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private sealed record FileRow(AgentFileEntry Source, string Type, string Name, string Size, string Modified, string Detail);
}
