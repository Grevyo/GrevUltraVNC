using System.Text.Json;
using GrevUltraVNC.Models;

namespace GrevUltraVNC.Services;

public sealed class JsonStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _appDataDirectory;
    private readonly string _machinesPath;
    private readonly string _settingsPath;

    public JsonStorage()
    {
        _appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrevUltraVNC");
        _machinesPath = Path.Combine(_appDataDirectory, "machines.json");
        _settingsPath = Path.Combine(_appDataDirectory, "settings.json");
    }

    public async Task<List<Machine>> LoadMachinesAsync()
    {
        if (!File.Exists(_machinesPath)) return [];
        await using var stream = File.OpenRead(_machinesPath);
        return await JsonSerializer.DeserializeAsync<List<Machine>>(stream, JsonOptions) ?? [];
    }

    public async Task SaveMachinesAsync(IEnumerable<Machine> machines)
    {
        Directory.CreateDirectory(_appDataDirectory);
        await using var stream = File.Create(_machinesPath);
        await JsonSerializer.SerializeAsync(stream, machines, JsonOptions);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        if (!File.Exists(_settingsPath)) return new AppSettings();
        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        Directory.CreateDirectory(_appDataDirectory);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }
}
