using System.Text.Json;
using LogTail.Core.Logging;
using LogTail.Core.Models;

namespace LogTail.Core.Persistence;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private readonly ILogTailLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore(string appDataDirectory, ILogTailLogger? logger = null)
    {
        _settingsPath = Path.Combine(appDataDirectory, "settings.json");
        _logger = logger ?? new ConsoleLogger();
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load settings, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save settings: {ex.Message}");
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        var current = Load();
        var updated = mutate(current);
        Save(updated);
    }
}
