using System;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppState _appState;
    private string _lastWorkflowStage = "Inventory";

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _currentViewName = "Inventory";

    [ObservableProperty]
    private double _uiScale = 1.0;

    [ObservableProperty]
    private bool _isDark = true;

    [ObservableProperty]
    private Symbol _themeSymbol = Symbol.DarkTheme;

    [ObservableProperty]
    private string _themeButtonText = "Dark Mode";

    [ObservableProperty]
    private bool _isPythonReady;

    [ObservableProperty]
    private string _pythonStatusText = "Checking Python...";

    [ObservableProperty]
    private bool _isExecutionRunning;

    [ObservableProperty]
    private string _inventoryStageSubtitle = "Select and validate Excel";

    [ObservableProperty]
    private string _plannerStageSubtitle = "Generate canary batches";

    [ObservableProperty]
    private string _executionStageSubtitle = "Ready to run";

    [ObservableProperty]
    private string _historyStageSubtitle = "StateStore and audit";

    // OS-aware navigation & window chrome
    public bool IsMacOS { get; }
    public bool IsWindowsOrLinux => !IsMacOS;
    public string PlatformShortcutPrefix => IsMacOS ? "⌘" : "Ctrl+";
    public string Stage1Tooltip => $"Inventory ({PlatformShortcutPrefix}1)";
    public string Stage2Tooltip => $"Plan & Batches ({PlatformShortcutPrefix}2)";
    public string Stage3Tooltip => $"Execute ({PlatformShortcutPrefix}3)";
    public string Stage4Tooltip => $"History ({PlatformShortcutPrefix}4)";
    public string CredentialsTooltip => $"Encrypted SSH Credentials Vault ({PlatformShortcutPrefix}K)";
    public string ConfigTooltip => $"Edit config.yaml parameters ({PlatformShortcutPrefix}E)";
    public string SettingsTooltip => $"Application theme, scaling, and Python engine ({PlatformShortcutPrefix},)";

    // Stage active indicators for horizontal stepper & titlebar utilities
    public bool IsInventoryActive => CurrentViewName == "Inventory";
    public bool IsPlannerActive => CurrentViewName == "Planner";
    public bool IsExecutionActive => CurrentViewName == "Execution";
    public bool IsHistoryActive => CurrentViewName == "History";
    public bool IsCredentialsActive => CurrentViewName == "Credentials";
    public bool IsConfigActive => CurrentViewName == "Config";
    public bool IsSettingsActive => CurrentViewName == "Settings";
    public bool IsUtilityViewActive => IsCredentialsActive || IsConfigActive || IsSettingsActive;

    // Stage completion indicators
    public bool IsInventoryCompleted => Inventory.HasValidationResult && Inventory.IsValid;
    public bool IsPlannerCompleted => Planner.HasPlan;

    // Safety lock: prevent changing inventory or plan while live upgrade is running
    public bool CanNavigateToSetupStages => !IsExecutionRunning;

    private DashboardViewModel? _dashboard;
    private ConfigViewModel? _config;

    public DashboardViewModel Dashboard => _dashboard ??= CreateDashboard();
    public InventoryViewModel Inventory { get; }
    public PlannerViewModel Planner { get; }
    public ExecutionViewModel Execution { get; }
    public HistoryViewModel History { get; }
    public CredentialsViewModel Credentials { get; }
    public ConfigViewModel Config => _config ??= new ConfigViewModel();
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel() : this(null)
    {
    }

    public MainWindowViewModel(bool? isMacOsOverride)
    {
        IsMacOS = isMacOsOverride ?? OperatingSystem.IsMacOS();
        _appState = AppState.Instance;

        Inventory = new InventoryViewModel();
        Planner = new PlannerViewModel();
        Execution = new ExecutionViewModel();
        History = new HistoryViewModel();
        Credentials = new CredentialsViewModel();
        Settings = new SettingsViewModel();

        // Start directly on Stage 1: Inventory & Setup
        _currentView = Inventory;
        _currentViewName = "Inventory";

        // Wire stage-to-stage pipeline transitions
        Inventory.ContinueToPlanRequested += inventoryPath =>
        {
            if (!string.IsNullOrWhiteSpace(inventoryPath))
            {
                Planner.InventoryFilePath = inventoryPath;
                Execution.InventoryFilePath = inventoryPath;
            }
            NavigateTo("Planner");
        };
        Inventory.OpenConfigRequested += () => NavigateTo("Config");
        Inventory.StateChanged += () =>
        {
            if (!string.IsNullOrWhiteSpace(Inventory.InventoryFilePath))
            {
                Planner.InventoryFilePath = Inventory.InventoryFilePath;
                if (!Execution.IsRunning)
                {
                    Execution.InventoryFilePath = Inventory.InventoryFilePath;
                }
            }
            Planner.UpdateStage1LinkSummary(Inventory.DeviceCount, Inventory.IsValid, Inventory.ErrorCount, Inventory.IsDirty);
            Execution.UpdateStage1LinkSummary(Inventory.DeviceCount, Inventory.IsValid, Inventory.ErrorCount, Inventory.IsDirty);
            UpdateStageSubtitles();
        };

        // Stage 2 (Plan) and Stage 3 (Execute) depend directly on Stage 1 as single source of truth
        Planner.EnsureInventorySyncedAsync = () => Inventory.EnsureSyncedForPipelineAsync();
        Execution.EnsureInventorySyncedAsync = () => Inventory.EnsureSyncedForPipelineAsync();
        Execution.BackToInventoryRequested += () =>
        {
            if (CanNavigateToSetupStages) NavigateTo("Inventory");
        };
        Execution.OpenCredentialsVaultRequested += () => NavigateTo("Credentials");

        // Sync AES-256 Encrypted Credential Vault presets straight into Stage 3 Execution
        Credentials.CredentialAppliedToSession += entry =>
        {
            Execution.ApplyCredentialPreset(entry);
        };
        Credentials.VaultChanged += () =>
        {
            Execution.SyncVaultPresets(Credentials.StoredCredentials);
        };

        Planner.BackToInventoryRequested += () =>
        {
            if (CanNavigateToSetupStages) NavigateTo("Inventory");
        };
        Planner.NavigateToExecutionRequested += jobId =>
        {
            if (!string.IsNullOrWhiteSpace(Planner.InventoryFilePath))
            {
                Execution.InventoryFilePath = Planner.InventoryFilePath;
            }
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                Execution.JobId = jobId;
            }
            NavigateTo("Execution");
        };
        Planner.StateChanged += UpdateStageSubtitles;

        Execution.BackToPlanRequested += () =>
        {
            if (CanNavigateToSetupStages) NavigateTo("Planner");
        };
        Execution.GoToHistoryRequested += () =>
        {
            _ = History.LoadJobsAsync();
            NavigateTo("History");
        };
        Execution.StateChanged += () =>
        {
            IsExecutionRunning = Execution.IsRunning;
            OnPropertyChanged(nameof(CanNavigateToSetupStages));
            UpdateStageSubtitles();
        };
        Execution.ExecutionCompleted += () =>
        {
            _ = History.LoadJobsAsync();
        };

        History.RequestExecution += jobId =>
        {
            Execution.JobId = jobId;
            Execution.SelectedModeIndex = 2; // Resume
            NavigateTo("Execution");
        };
        History.BackToExecutionRequested += () => NavigateTo("Execution");
        History.StateChanged += UpdateStageSubtitles;

        Settings.DiagnosticsCompleted += (ready, status) =>
        {
            IsPythonReady = ready;
            PythonStatusText = status;
        };

        // Theme & scale sync
        _appState.ThemeChanged += OnThemeChanged;
        _appState.UiScaleChanged += scale => UiScale = scale;
        UiScale = _appState.UiScale;

        UpdateThemeState(_appState.CurrentThemeIndex);
        Planner.UpdateStage1LinkSummary(Inventory.DeviceCount, Inventory.IsValid, Inventory.ErrorCount, Inventory.IsDirty);
        Execution.UpdateStage1LinkSummary(Inventory.DeviceCount, Inventory.IsValid, Inventory.ErrorCount, Inventory.IsDirty);
        UpdateStageSubtitles();
    }

    private DashboardViewModel CreateDashboard()
    {
        var dash = new DashboardViewModel();
        dash.NavigateRequested += nav => NavigateTo(nav);
        return dash;
    }

    private void UpdateStageSubtitles()
    {
        if (Inventory.HasValidationResult)
        {
            InventoryStageSubtitle = Inventory.IsValid
                ? $"{Inventory.DeviceCount} device(s) validated"
                : $"{Inventory.ErrorCount} validation issue(s)";
        }
        else if (Inventory.DeviceCount > 0)
        {
            InventoryStageSubtitle = $"{Inventory.DeviceCount} device(s) loaded";
        }
        else
        {
            InventoryStageSubtitle = "Select and validate Excel";
        }

        if (Planner.HasPlan)
        {
            PlannerStageSubtitle = $"{Planner.Batches.Count} batch(es) · {Planner.PlanJobId}";
        }
        else
        {
            PlannerStageSubtitle = "Generate canary batches";
        }

        if (Execution.IsRunning)
        {
            ExecutionStageSubtitle = $"Running · {Execution.ElapsedTime}";
        }
        else if (Execution.HasCompletedRun)
        {
            ExecutionStageSubtitle = Execution.LastRunSucceeded
                ? $"Completed · {Execution.ElapsedTime}"
                : "Ended with issues";
        }
        else if (!string.IsNullOrWhiteSpace(Execution.JobId))
        {
            ExecutionStageSubtitle = $"Job {Execution.JobId} queued";
        }
        else
        {
            ExecutionStageSubtitle = "Ready to run";
        }

        HistoryStageSubtitle = History.TotalJobs > 0
            ? $"{History.TotalJobs} job(s) recorded"
            : "StateStore and audit";

        OnPropertyChanged(nameof(IsInventoryCompleted));
        OnPropertyChanged(nameof(IsPlannerCompleted));
    }

    public void NavigateTo(string viewName)
    {
        // Guard setup stages while a live upgrade is actively running
        if (IsExecutionRunning && (viewName == "Inventory" || viewName == "Planner"))
        {
            return;
        }

        CurrentViewName = viewName;

        if (viewName is "Inventory" or "Planner" or "Execution" or "History")
        {
            _lastWorkflowStage = viewName;
        }

        if (viewName == "History")
        {
            _ = History.LoadJobsAsync();
        }

        CurrentView = viewName switch
        {
            "Dashboard" => Dashboard,
            "Inventory" => Inventory,
            "Planner" => Planner,
            "Execution" => Execution,
            "History" => History,
            "Credentials" => Credentials,
            "Config" => Config,
            "Settings" => Settings,
            _ => Inventory
        };

        OnPropertyChanged(nameof(IsInventoryActive));
        OnPropertyChanged(nameof(IsPlannerActive));
        OnPropertyChanged(nameof(IsExecutionActive));
        OnPropertyChanged(nameof(IsHistoryActive));
        OnPropertyChanged(nameof(IsCredentialsActive));
        OnPropertyChanged(nameof(IsConfigActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsUtilityViewActive));
    }

    [RelayCommand]
    public void SelectStage1() => NavigateTo("Inventory");

    [RelayCommand]
    public void SelectStage2() => NavigateTo("Planner");

    [RelayCommand]
    public void SelectStage3() => NavigateTo("Execution");

    [RelayCommand]
    public void SelectStage4() => NavigateTo("History");

    [RelayCommand]
    public void OpenCredentials() => NavigateTo("Credentials");

    [RelayCommand]
    public void OpenConfig() => NavigateTo("Config");

    [RelayCommand]
    public void OpenSettings() => NavigateTo("Settings");

    [RelayCommand]
    public void ReturnToWorkflow() => NavigateTo(_lastWorkflowStage);

    [RelayCommand]
    public void AddSwitchFromShortcut()
    {
        if (!CanNavigateToSetupStages) return;
        NavigateTo("Inventory");
        Inventory.AddSwitchCommand.Execute(null);
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        int next = (_appState.CurrentThemeIndex + 1) % 3;
        _appState.SetTheme(next);
    }

    [RelayCommand]
    public void ZoomIn() => _appState.SetUiScale(UiScale + 0.1);

    [RelayCommand]
    public void ZoomOut() => _appState.SetUiScale(UiScale - 0.1);

    [RelayCommand]
    public void ResetZoom() => _appState.SetUiScale(1.0);

    private void OnThemeChanged(int themeIndex)
    {
        UpdateThemeState(themeIndex);
    }

    public void UpdateThemeState(int themeIndex)
    {
        if (themeIndex == 2)
        {
            IsDark = false;
            ThemeSymbol = Symbol.Highlight;
            ThemeButtonText = "Light Mode";
        }
        else if (themeIndex == 1)
        {
            IsDark = true;
            ThemeSymbol = Symbol.DarkTheme;
            ThemeButtonText = "Dark Mode";
        }
        else // System Default
        {
            bool isActuallyDark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
            IsDark = isActuallyDark;
            ThemeSymbol = isActuallyDark ? Symbol.DarkTheme : Symbol.Highlight;
            ThemeButtonText = isActuallyDark ? "Dark (System)" : "Light (System)";
        }
    }
}
