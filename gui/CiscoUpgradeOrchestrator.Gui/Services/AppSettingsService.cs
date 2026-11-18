using System;
using System.IO;
using System.Text.Json;

namespace CiscoUpgradeOrchestrator.Gui.Services;

public class AppSettingsData
{
    public string PythonExecutable { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public int ThemeIndex { get; set; } = 1;
    public double UiScale { get; set; } = 1.0;
}

public static class AppSettingsService
{
    private static readonly object SyncLock = new();
    private static AppSettingsData? _cached;

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CiscoUpgradeOrchestrator");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettingsData Load()
    {
        lock (SyncLock)
        {
            if (_cached != null)
            {
                return new AppSettingsData
                {
                    PythonExecutable = _cached.PythonExecutable,
                    ProjectRoot = _cached.ProjectRoot,
                    ThemeIndex = _cached.ThemeIndex,
                    UiScale = _cached.UiScale
                };
            }

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                    if (data != null)
                    {
                        _cached = data;
                        return new AppSettingsData
                        {
                            PythonExecutable = data.PythonExecutable,
                            ProjectRoot = data.ProjectRoot,
                            ThemeIndex = data.ThemeIndex,
                            UiScale = data.UiScale
                        };
                    }
                }
            }
            catch { }

            _cached = new AppSettingsData();
            return new AppSettingsData();
        }
    }

    public static void Save(AppSettingsData data)
    {
        lock (SyncLock)
        {
            _cached = new AppSettingsData
            {
                PythonExecutable = data.PythonExecutable,
                ProjectRoot = data.ProjectRoot,
                ThemeIndex = data.ThemeIndex,
                UiScale = data.UiScale
            };

            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }
                var json = JsonSerializer.Serialize(_cached, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }
}
