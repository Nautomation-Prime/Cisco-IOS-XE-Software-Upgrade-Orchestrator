using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private readonly PythonBackendService _backend;

    [ObservableProperty]
    private string _configText = string.Empty;

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ConfigViewModel()
    {
        _backend = PythonBackendService.Instance;
        ConfigPath = Path.Combine(_backend.ProjectRoot, "config", "config.yaml");
        _ = LoadConfigAsync();
    }

    [RelayCommand]
    public async Task LoadConfigAsync()
    {
        IsLoading = true;
        try
        {
            if (File.Exists(ConfigPath))
            {
                ConfigText = await File.ReadAllTextAsync(ConfigPath);
                StatusMessage = "Configuration loaded from config.yaml";
            }
            else
            {
                var examplePath = Path.Combine(_backend.ProjectRoot, "config", "config.example.yaml");
                if (File.Exists(examplePath))
                {
                    ConfigText = await File.ReadAllTextAsync(examplePath);
                    StatusMessage = "Loaded default template from config.example.yaml";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading config: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        IsLoading = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            await File.WriteAllTextAsync(ConfigPath, ConfigText);
            StatusMessage = "Configuration saved successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving config: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateEnvTemplateAsync()
    {
        IsLoading = true;
        try
        {
            var target = Path.Combine(_backend.ProjectRoot, ".env.example");
            var tcs = new TaskCompletionSource<bool>();
            await _backend.RunOrchestratorStreamingAsync(
                $"create-env -o \"{target}\"",
                line => StatusMessage = line,
                (ok, code) => tcs.SetResult(ok),
                default
            );
            await tcs.Task;
            StatusMessage = $"Created .env.example at {target}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
