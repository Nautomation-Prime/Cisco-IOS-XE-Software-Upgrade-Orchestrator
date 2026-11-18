using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class ExecutionViewModel : ViewModelBase
{
    private const int MaxConsoleChars = 400_000;

    private readonly PythonBackendService _backend;
    private readonly Stopwatch _stopwatch = new();
    private readonly StringBuilder _consoleBuffer = new(8192);
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _inventoryFilePath = string.Empty;

    [ObservableProperty]
    private string _stage1SourceSummary = "Linked to Stage 1 switch inventory";

    [ObservableProperty]
    private bool _isStage1Ready;

    [ObservableProperty]
    private string _jobId = string.Empty;

    [ObservableProperty]
    private int _selectedModeIndex = 0; // 0 = Upgrade, 1 = Stage, 2 = Resume

    [ObservableProperty]
    private bool _dryRun = false;

    [ObservableProperty]
    private bool _autoApprove = true;

    [ObservableProperty]
    private bool _retryFailed = false;

    [ObservableProperty]
    private int _autoAbortTimerMinutes = 120;

    // Required in-GUI SSH session credentials (injected via env vars, zero .env dependency)
    [ObservableProperty]
    private string _sessionUsername = string.Empty;

    [ObservableProperty]
    private string _sessionPassword = string.Empty;

    [ObservableProperty]
    private string _sessionEnableSecret = string.Empty;

    [ObservableProperty]
    private string _activeCredentialLabel = string.Empty;

    [ObservableProperty]
    private string _usernameError = string.Empty;

    [ObservableProperty]
    private string _passwordError = string.Empty;

    [ObservableProperty]
    private string _jobIdError = string.Empty;

    [ObservableProperty]
    private ObservableCollection<CredentialEntry> _vaultPresets = new();

    [ObservableProperty]
    private CredentialEntry? _selectedVaultPreset;

    public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);
    public bool HasJobIdError => !string.IsNullOrEmpty(JobIdError);
    public bool HasVaultPresets => VaultPresets.Count > 0;
    public bool HasActiveCredentialLabel => !string.IsNullOrEmpty(ActiveCredentialLabel);

    public Action? OpenCredentialsVaultRequested;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _lastRunSucceeded;

    [ObservableProperty]
    private bool _hasCompletedRun;

    [ObservableProperty]
    private string _executionStatus = "Idle — ready to run";

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    private string _consoleOutput = string.Empty;

    public bool HasConsoleOutput => !string.IsNullOrEmpty(ConsoleOutput);
    public bool CanUseDryRun => !IsRunning && SelectedModeIndex == 0;
    public bool CanUseAutoApprove => !IsRunning && SelectedModeIndex != 2;

    public Func<Task<(bool ok, string path, string? error)>>? EnsureInventorySyncedAsync { get; set; }
    public Action? BackToInventoryRequested;
    public Action? BackToPlanRequested;
    public Action? GoToHistoryRequested;
    public event Action? StateChanged;
    public event Action? ExecutionCompleted;

    public void UpdateStage1LinkSummary(int deviceCount, bool isValid, int errorCount, bool isDirty)
    {
        if (deviceCount == 0)
        {
            IsStage1Ready = false;
            Stage1SourceSummary = "No switches configured in Stage 1";
        }
        else if (!isValid)
        {
            IsStage1Ready = false;
            Stage1SourceSummary = $"Stage 1 has {errorCount} issue(s) across {deviceCount} switch(es)";
        }
        else
        {
            IsStage1Ready = true;
            Stage1SourceSummary = isDirty
                ? $"Stage 1 · {deviceCount} switch(es) (auto-syncs on start)"
                : $"Stage 1 · {deviceCount} switch(es) ready";
        }
    }

    [RelayCommand]
    public void EditSwitchesInStage1()
    {
        if (!IsRunning)
        {
            BackToInventoryRequested?.Invoke();
        }
    }

    public ExecutionViewModel()
    {
        _backend = PythonBackendService.Instance;
        InventoryFilePath = Path.Combine(_backend.ProjectRoot, "inventory", "gui_inventory.xlsx");
    }

    partial void OnSessionUsernameChanged(string value)
    {
        if (value is null) _sessionUsername = string.Empty;
        if (HasUsernameError && !string.IsNullOrWhiteSpace(_sessionUsername))
        {
            UsernameError = string.Empty;
            OnPropertyChanged(nameof(HasUsernameError));
        }
    }

    partial void OnSessionPasswordChanged(string value)
    {
        if (value is null) _sessionPassword = string.Empty;
        if (HasPasswordError && !string.IsNullOrEmpty(_sessionPassword))
        {
            PasswordError = string.Empty;
            OnPropertyChanged(nameof(HasPasswordError));
        }
    }

    partial void OnJobIdChanged(string value)
    {
        if (value is null) _jobId = string.Empty;
        if (HasJobIdError && !string.IsNullOrWhiteSpace(_jobId))
        {
            JobIdError = string.Empty;
            OnPropertyChanged(nameof(HasJobIdError));
        }
    }

    partial void OnActiveCredentialLabelChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveCredentialLabel));
    }

    partial void OnSelectedVaultPresetChanged(CredentialEntry? value)
    {
        if (value != null)
        {
            ApplyCredentialPreset(value);
        }
    }

    public void ApplyCredentialPreset(CredentialEntry entry)
    {
        SessionUsername = entry.Username ?? string.Empty;
        SessionPassword = entry.Password ?? string.Empty;
        SessionEnableSecret = entry.Secret ?? string.Empty;
        ActiveCredentialLabel = $"Preset: {entry.Name}";
        UsernameError = string.Empty;
        PasswordError = string.Empty;
        OnPropertyChanged(nameof(HasUsernameError));
        OnPropertyChanged(nameof(HasPasswordError));
    }

    public void SyncVaultPresets(IEnumerable<CredentialEntry> entries)
    {
        VaultPresets.Clear();
        foreach (var e in entries)
        {
            VaultPresets.Add(e);
        }
        OnPropertyChanged(nameof(HasVaultPresets));
    }

    public bool ValidateExecutionInputs()
    {
        UsernameError = string.IsNullOrWhiteSpace(SessionUsername)
            ? "SSH username is required"
            : string.Empty;

        PasswordError = string.IsNullOrEmpty(SessionPassword)
            ? "SSH password is required"
            : string.Empty;

        JobIdError = SelectedModeIndex == 2 && string.IsNullOrWhiteSpace(JobId)
            ? "Job ID is required to resume an interrupted job"
            : string.Empty;

        OnPropertyChanged(nameof(HasUsernameError));
        OnPropertyChanged(nameof(HasPasswordError));
        OnPropertyChanged(nameof(HasJobIdError));

        return !HasUsernameError && !HasPasswordError && !HasJobIdError;
    }

    [RelayCommand]
    public void OpenCredentialsVault()
    {
        OpenCredentialsVaultRequested?.Invoke();
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        // Only `upgrade` supports --dry-run
        if (value != 0 && DryRun)
        {
            DryRun = false;
        }
        OnPropertyChanged(nameof(CanUseDryRun));
        OnPropertyChanged(nameof(CanUseAutoApprove));
    }

    partial void OnDryRunChanged(bool value)
    {
        if (value && SelectedModeIndex != 0)
        {
            DryRun = false;
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseDryRun));
        OnPropertyChanged(nameof(CanUseAutoApprove));
        StateChanged?.Invoke();
    }

    partial void OnConsoleOutputChanged(string value)
    {
        OnPropertyChanged(nameof(HasConsoleOutput));
    }

    partial void OnElapsedTimeChanged(string value)
    {
        if (IsRunning)
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Build a strictly valid orchestrator.py CLI command matching click option schemas:
    /// - upgrade: [--file | --job-id] [--dry-run] [--yes] [--retry-failed]
    /// - stage:   [--file | --job-id] [--yes] [--retry-failed]  (no --dry-run)
    /// - resume:  --job-id [--retry-failed]                     (no --yes, no --dry-run)
    /// </summary>
    public string? BuildCommandArguments(out string? validationError, out string? infoNote)
    {
        validationError = null;
        infoNote = null;

        var sbArgs = new StringBuilder();
        string mode = SelectedModeIndex switch
        {
            1 => "stage",
            2 => "resume",
            _ => "upgrade"
        };

        sbArgs.Append(mode);

        var trimmedJobId = JobId?.Trim() ?? string.Empty;
        var trimmedInventory = InventoryFilePath?.Trim() ?? string.Empty;

        if (mode == "resume")
        {
            if (string.IsNullOrWhiteSpace(trimmedJobId))
            {
                validationError = "Job ID is required to resume an interrupted job";
                return null;
            }
            sbArgs.Append($" --job-id \"{trimmedJobId}\"");
            if (RetryFailed) sbArgs.Append(" --retry-failed");
            return sbArgs.ToString();
        }

        if (mode == "upgrade" && DryRun)
        {
            // orchestrator.py forbids --dry-run on an existing non-dry-run planned job.
            // Always start a fresh dry-run job from the inventory file when DryRun is enabled.
            if (string.IsNullOrWhiteSpace(trimmedInventory))
            {
                validationError = "An inventory file path is required for Dry Run mode";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(trimmedJobId))
            {
                infoNote = $"[INFO] Dry Run enabled: creating a read-only dry-run job from inventory instead of mutating planned job {trimmedJobId}.";
            }
            sbArgs.Append($" -f \"{trimmedInventory}\" --dry-run");
        }
        else if (!string.IsNullOrWhiteSpace(trimmedJobId))
        {
            sbArgs.Append($" --job-id \"{trimmedJobId}\"");
        }
        else if (!string.IsNullOrWhiteSpace(trimmedInventory))
        {
            sbArgs.Append($" -f \"{trimmedInventory}\"");
        }
        else
        {
            validationError = "Provide an inventory file or an existing Job ID";
            return null;
        }

        if (AutoApprove) sbArgs.Append(" --yes");
        if (RetryFailed) sbArgs.Append(" --retry-failed");

        return sbArgs.ToString();
    }

    private void FlushPendingLogs()
    {
        if (_pendingLines.IsEmpty) return;

        bool appended = false;
        while (_pendingLines.TryDequeue(out var line))
        {
            _consoleBuffer.AppendLine(line);
            appended = true;
        }

        if (!appended) return;

        // Enforce rolling window cap so very long upgrades never stall layout
        if (_consoleBuffer.Length > MaxConsoleChars)
        {
            int removeCount = _consoleBuffer.Length - (MaxConsoleChars * 3 / 4);
            _consoleBuffer.Remove(0, removeCount);
        }

        ConsoleOutput = _consoleBuffer.ToString();
    }

    [RelayCommand]
    public async Task StartExecutionAsync()
    {
        if (IsRunning) return;

        // Validate required GUI SSH credentials & Resume Job ID before launching Python
        if (!ValidateExecutionInputs())
        {
            ExecutionStatus = "Enter required SSH credentials before starting execution";
            return;
        }

        // If starting a fresh run or dry-run from Stage 1 switches, auto-sync Stage 1 cards to .xlsx first
        bool needsInventorySync = SelectedModeIndex != 2 && (DryRun || string.IsNullOrWhiteSpace(JobId));
        if (needsInventorySync && EnsureInventorySyncedAsync != null)
        {
            ExecutionStatus = "Syncing Stage 1 switch inventory...";
            var (ok, syncedPath, error) = await EnsureInventorySyncedAsync();
            if (!ok)
            {
                ExecutionStatus = error ?? "Fix Stage 1 switch validation errors before starting execution";
                return;
            }
            InventoryFilePath = syncedPath;
        }

        var args = BuildCommandArguments(out var validationError, out var infoNote);
        if (args == null)
        {
            ExecutionStatus = validationError ?? "Invalid execution configuration";
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Clear buffer & pending queue
        while (_pendingLines.TryDequeue(out _)) { }
        _consoleBuffer.Clear();
        if (!string.IsNullOrEmpty(infoNote))
        {
            _consoleBuffer.AppendLine(infoNote);
        }
        _consoleBuffer.AppendLine($"[START] python orchestrator.py {args}");
        ConsoleOutput = _consoleBuffer.ToString();

        IsRunning = true;
        HasCompletedRun = false;
        _stopwatch.Restart();
        ElapsedTime = "00:00";

        string modeLabel = SelectedModeIndex switch
        {
            1 => "stage",
            2 => "resume",
            _ => DryRun ? "dry-run" : "upgrade"
        };
        ExecutionStatus = $"Running {modeLabel} workflow";
        StateChanged?.Invoke();

        // Batched UI log flusher (100ms) + elapsed timer (1s) tied cleanly to execution lifetime
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var uiLoopTask = Task.Run(async () =>
        {
            int tick = 0;
            try
            {
                while (!loopCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, loopCts.Token);
                    tick++;
                    bool updateTimer = (tick % 10 == 0);
                    bool hasLogs = !_pendingLines.IsEmpty;

                    if (hasLogs || updateTimer)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (hasLogs) FlushPendingLogs();
                            if (updateTimer && IsRunning)
                            {
                                var ts = _stopwatch.Elapsed;
                                ElapsedTime = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, loopCts.Token);

        var sessionEnv = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(SessionUsername))
            sessionEnv["DEVICE_USERNAME"] = SessionUsername.Trim();
        if (!string.IsNullOrEmpty(SessionPassword))
            sessionEnv["DEVICE_PASSWORD"] = SessionPassword;
        if (!string.IsNullOrEmpty(SessionEnableSecret))
            sessionEnv["DEVICE_ENABLE_PASSWORD"] = SessionEnableSecret;

        try
        {
            await _backend.RunOrchestratorStreamingAsync(
                args,
                line => _pendingLines.Enqueue(line),
                (success, exitCode) =>
                {
                    loopCts.Cancel();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        IsRunning = false;
                        HasCompletedRun = true;
                        LastRunSucceeded = success;
                        _stopwatch.Stop();
                        var ts = _stopwatch.Elapsed;
                        ElapsedTime = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
                        ExecutionStatus = success
                            ? $"Completed (exit {exitCode}) in {ElapsedTime}"
                            : $"Ended with exit code {exitCode}";
                        _pendingLines.Enqueue(string.Empty);
                        _pendingLines.Enqueue($"[FINISH] {ExecutionStatus}");
                        FlushPendingLogs();
                        StateChanged?.Invoke();
                        ExecutionCompleted?.Invoke();
                    });
                },
                token,
                sessionEnv.Count > 0 ? sessionEnv : null
            );
        }
        finally
        {
            loopCts.Cancel();
            try { await uiLoopTask; } catch { }
        }
    }

    [RelayCommand]
    public void CancelExecution()
    {
        if (!IsRunning || _cts == null) return;
        _cts.Cancel();
        ExecutionStatus = "Cancelling execution";
        StateChanged?.Invoke();
    }

    [RelayCommand]
    public void ClearConsole()
    {
        while (_pendingLines.TryDequeue(out _)) { }
        _consoleBuffer.Clear();
        ConsoleOutput = string.Empty;
    }

    [RelayCommand]
    public void BackToPlan()
    {
        if (!IsRunning)
        {
            BackToPlanRequested?.Invoke();
        }
    }

    [RelayCommand]
    public void GoToHistory()
    {
        GoToHistoryRequested?.Invoke();
    }
}
