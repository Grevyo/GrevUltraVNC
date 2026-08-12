using System.IO;
using System.Text.Json;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed class AdminWorkspaceStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const int MaxActivityEntries = 1000;

    private readonly string _appDataDirectory;
    private readonly string _savedCommandsPath;
    private readonly string _activityPath;

    public AdminWorkspaceStorage()
    {
        _appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrevUltraVNC");
        _savedCommandsPath = Path.Combine(_appDataDirectory, "saved-commands.json");
        _activityPath = Path.Combine(_appDataDirectory, "activity.json");
    }

    public async Task<List<SavedCommand>> LoadSavedCommandsAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return await ReadListAsync<SavedCommand>(_savedCommandsPath);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveSavedCommandsAsync(IEnumerable<SavedCommand> commands)
    {
        await Gate.WaitAsync();
        try
        {
            var ordered = commands
                .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await WriteListAsync(_savedCommandsPath, ordered);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<List<ActivityEntry>> LoadActivityAsync(Guid machineId)
    {
        await Gate.WaitAsync();
        try
        {
            var entries = await ReadListAsync<ActivityEntry>(_activityPath);
            return entries
                .Where(entry => entry.MachineId == machineId)
                .OrderByDescending(entry => entry.TimestampUtc)
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task AppendActivityAsync(ActivityEntry entry)
    {
        await Gate.WaitAsync();
        try
        {
            var entries = await ReadListAsync<ActivityEntry>(_activityPath);
            entries.Add(entry);
            entries = entries
                .OrderByDescending(item => item.TimestampUtc)
                .Take(MaxActivityEntries)
                .OrderBy(item => item.TimestampUtc)
                .ToList();
            await WriteListAsync(_activityPath, entries);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ClearActivityAsync(Guid machineId)
    {
        await Gate.WaitAsync();
        try
        {
            var entries = await ReadListAsync<ActivityEntry>(_activityPath);
            entries.RemoveAll(entry => entry.MachineId == machineId);
            await WriteListAsync(_activityPath, entries);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<List<T>> ReadListAsync<T>(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteListAsync<T>(string path, IReadOnlyCollection<T> items)
    {
        Directory.CreateDirectory(_appDataDirectory);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions);

        File.Move(tempPath, path, overwrite: true);
    }
}
