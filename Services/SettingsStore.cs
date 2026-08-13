using System.Text.Json;
using System.IO;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WallhavenService",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath));
                if (settings is not null)
                    return settings;
            }
        }
        catch
        {
            // Corrupt settings are replaced with defaults on the next save.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
