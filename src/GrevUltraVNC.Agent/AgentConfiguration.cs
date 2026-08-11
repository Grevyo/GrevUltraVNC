using System.Text.Json;
using GrevUltraVNC.Contracts;

namespace GrevUltraVNC.Agent;

public sealed class AgentConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Port { get; set; } = AgentProtocol.DefaultPort;
    public int UltraVncPort { get; set; } = 5900;
    public string SharedKey { get; set; } = AgentProtocol.CreateSharedKey();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GrevUltraVNC",
        "Agent");

    public static string ConfigPath => Path.Combine(DataDirectory, "agent.json");

    public static AgentConfiguration LoadOrCreate()
    {
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(ConfigPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<AgentConfiguration>(File.ReadAllText(ConfigPath), JsonOptions);
                if (existing is not null &&
                    existing.Port is >= 1 and <= 65535 &&
                    existing.UltraVncPort is >= 1 and <= 65535 &&
                    AgentProtocol.IsValidSharedKey(existing.SharedKey))
                    return existing;
            }
            catch
            {
                // A malformed file is replaced below with a fresh valid configuration.
            }
        }

        var configuration = new AgentConfiguration();
        Save(configuration);
        return configuration;
    }

    public static void Save(AgentConfiguration configuration)
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temp, ConfigPath, overwrite: true);
    }
}
