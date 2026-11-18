using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public class DependencyItemModel
{
    public string Name { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public string StatusText => Installed ? "Installed" : "Missing";
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppState _appState;
    private readonly PythonBackendService _backend;
    private bool _isInitializing = true;
    private System.Threading.CancellationTokenSource? _scaleSaveDebounceCts;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private double _uiScale;

    [ObservableProperty]
    private string _pythonExecutable = string.Empty;

    [ObservableProperty]
    private string _projectRoot = string.Empty;

    [ObservableProperty]
    private string _pythonVersion = string.Empty;

    [ObservableProperty]
    private string _testStatus = "Ready to verify Python runtime";

    [ObservableProperty]
    private string _lastErrorDetails = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private ObservableCollection<DependencyItemModel> _dependencies = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableInterpreters = new();

    public Symbol StatusSymbol => HasError ? Symbol.Important : Symbol.Checkmark;

    // Delegates for File/Folder pickers wired by View code-behind
    public Func<Task<string?>>? PickPythonFileHandler { get; set; }
    public Func<Task<string?>>? PickFolderHandler { get; set; }

    public event Action<bool, string>? DiagnosticsCompleted;

    public SettingsViewModel()
    {
        _appState = AppState.Instance;
        _backend = PythonBackendService.Instance;

        PythonExecutable = _backend.PythonPath;
        ProjectRoot = _backend.ProjectRoot;
        ThemeIndex = _appState.CurrentThemeIndex;
        UiScale = _appState.UiScale;

        _isInitializing = false;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await PopulateInterpretersAsync();
        await RunDiagnosticsAsync();
    }

    partial void OnHasErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusSymbol));
    }

    public void PopulateInterpreters()
    {
        var list = _backend.FindAvailablePythonInterpreters();
        AvailableInterpreters.Clear();
        foreach (var item in list)
        {
            AvailableInterpreters.Add(item);
        }
    }

    public async Task PopulateInterpretersAsync()
    {
        var list = await Task.Run(() => _backend.FindAvailablePythonInterpreters());
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AvailableInterpreters.Clear();
            foreach (var item in list)
            {
                AvailableInterpreters.Add(item);
            }
        });
    }

    partial void OnThemeIndexChanged(int value)
    {
        _appState.SetTheme(value);
        if (!_isInitializing) SaveCurrentSettings();
    }

    partial void OnUiScaleChanged(double value)
    {
        _appState.SetUiScale(value);
        if (!_isInitializing)
        {
            // Debounce slider writes so dragging zoom doesn't thrash disk I/O
            _scaleSaveDebounceCts?.Cancel();
            _scaleSaveDebounceCts?.Dispose();
            _scaleSaveDebounceCts = new System.Threading.CancellationTokenSource();
            var ct = _scaleSaveDebounceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, ct);
                    SaveCurrentSettings();
                }
                catch (OperationCanceledException) { }
            }, ct);
        }
    }

    [RelayCommand]
    public void ResetUiScale()
    {
        UiScale = 1.0;
        SaveCurrentSettings();
    }

    [RelayCommand]
    public async Task AutoDetectPythonAsync()
    {
        IsTesting = true;
        TestStatus = "Auto-detecting Python virtual environment";
        HasError = false;
        LastErrorDetails = string.Empty;

        try
        {
            PopulateInterpreters();
            var detected = _backend.AutoDetectPythonInterpreter();
            if (!string.IsNullOrEmpty(detected))
            {
                PythonExecutable = detected;
            }

            var detectedRoot = _backend.DiscoverProjectRoot();
            if (!string.IsNullOrEmpty(detectedRoot))
            {
                ProjectRoot = detectedRoot;
            }

            await RunDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            IsReady = false;
            TestStatus = $"Auto-detect error: {ex.Message}";
            LastErrorDetails = ex.ToString();
            DiagnosticsCompleted?.Invoke(false, "Python check failed");
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    public async Task BrowsePythonAsync()
    {
        if (PickPythonFileHandler != null)
        {
            var selected = await PickPythonFileHandler();
            if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
            {
                PythonExecutable = selected;
                await RunDiagnosticsAsync();
            }
        }
    }

    [RelayCommand]
    public async Task BrowseProjectRootAsync()
    {
        if (PickFolderHandler != null)
        {
            var selected = await PickFolderHandler();
            if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(selected))
            {
                ProjectRoot = selected;
                await RunDiagnosticsAsync();
            }
        }
    }

    [RelayCommand]
    public async Task RunDiagnosticsAsync()
    {
        IsTesting = true;
        HasError = false;
        LastErrorDetails = string.Empty;
        TestStatus = "Testing Python runtime and networking packages";

        try
        {
            _backend.PythonPath = PythonExecutable;
            _backend.ProjectRoot = ProjectRoot;
            SaveCurrentSettings();

            var env = await _backend.TestEnvironmentAsync();
            if (env != null)
            {
                PythonVersion = env.PythonVersion;
                Dependencies.Clear();
                foreach (var kvp in env.Dependencies)
                {
                    Dependencies.Add(new DependencyItemModel
                    {
                        Name = kvp.Key,
                        Installed = kvp.Value
                    });
                }

                var shortVersion = env.PythonVersion.Split(' ')[0];
                if (env.AllInstalled)
                {
                    HasError = false;
                    IsReady = true;
                    TestStatus = $"Python {shortVersion} active — all core networking packages verified";
                    LastErrorDetails = string.Empty;
                    DiagnosticsCompleted?.Invoke(true, $"Python {shortVersion} ready");
                }
                else
                {
                    HasError = true;
                    IsReady = false;
                    TestStatus = "Missing Python dependencies — run pip install -r requirements.txt";
                    LastErrorDetails = !string.IsNullOrEmpty(env.ImportError)
                        ? $"Import error: {env.ImportError}"
                        : "Missing packages: " + string.Join(", ", env.Dependencies);
                    DiagnosticsCompleted?.Invoke(false, "Missing packages");
                }
            }
            else
            {
                HasError = true;
                IsReady = false;
                TestStatus = "Python bridge check failed — verify interpreter path and project root";
                LastErrorDetails = _backend.LastError ?? _backend.LastStderr ?? "Unknown process startup or execution error.";
                DiagnosticsCompleted?.Invoke(false, "Python not connected");
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            IsReady = false;
            TestStatus = $"Diagnostic error: {ex.Message}";
            LastErrorDetails = ex.ToString();
            DiagnosticsCompleted?.Invoke(false, "Python check failed");
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void SaveCurrentSettings()
    {
        _backend.SaveConfiguration(PythonExecutable, ProjectRoot);
        var settings = AppSettingsService.Load();
        settings.PythonExecutable = PythonExecutable;
        settings.ProjectRoot = ProjectRoot;
        settings.ThemeIndex = ThemeIndex;
        settings.UiScale = UiScale;
        AppSettingsService.Save(settings);
    }
}
