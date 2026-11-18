using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public class BatchCardModel
{
    public int BatchNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCanary { get; set; }
    public List<string> Devices { get; set; } = new();
    public int DeviceCount => Devices.Count;
    public string RoleLabel => IsCanary ? "Canary" : "Production";
}

public partial class PlannerViewModel : ViewModelBase
{
    private readonly PythonBackendService _backend;

    [ObservableProperty]
    private string _inventoryFilePath = string.Empty;

    [ObservableProperty]
    private string _stage1SourceSummary = "Linked to Stage 1 switch inventory";

    [ObservableProperty]
    private bool _isStage1Ready;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private string _planJobId = string.Empty;

    [ObservableProperty]
    private string _summaryText = "Click 'Generate plan' to sync Stage 1 switch cards and build topology-isolated canary batches.";

    [ObservableProperty]
    private ObservableCollection<BatchCardModel> _batches = new();

    [ObservableProperty]
    private ObservableCollection<string> _commands = new();

    public bool IsStage1ReadyReadOnly => IsStage1Ready;
    public string InventoryFileDisplayName => string.IsNullOrWhiteSpace(InventoryFilePath)
        ? "gui_inventory.xlsx"
        : Path.GetFileName(InventoryFilePath);

    partial void OnInventoryFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(InventoryFileDisplayName));
    }

    public Func<Task<(bool ok, string path, string? error)>>? EnsureInventorySyncedAsync { get; set; }
    public Action<string>? NavigateToExecutionRequested;
    public Action? BackToInventoryRequested;
    public event Action? StateChanged;

    public PlannerViewModel()
    {
        _backend = PythonBackendService.Instance;
        InventoryFilePath = Path.Combine(_backend.ProjectRoot, "inventory", "gui_inventory.xlsx");
    }

    public void UpdateStage1LinkSummary(int deviceCount, bool isValid, int errorCount, bool isDirty)
    {
        if (deviceCount == 0)
        {
            IsStage1Ready = false;
            Stage1SourceSummary = "No switches in Stage 1 yet — add switches in Inventory first";
        }
        else if (!isValid)
        {
            IsStage1Ready = false;
            Stage1SourceSummary = $"Stage 1 has {errorCount} validation issue(s) across {deviceCount} switch(es)";
        }
        else
        {
            IsStage1Ready = true;
            Stage1SourceSummary = isDirty
                ? $"Linked to Stage 1 · {deviceCount} switch(es) ready (auto-saves on plan)"
                : $"Linked to Stage 1 · {deviceCount} switch(es) synced";
        }
    }

    [RelayCommand]
    public async Task GeneratePlanAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        SummaryText = "Syncing Stage 1 switch cards and generating canary batch plan...";
        try
        {
            string targetFile = InventoryFilePath;

            // Always sync Stage 1 cards to .xlsx first so Stage 2 never runs on stale data
            if (EnsureInventorySyncedAsync != null)
            {
                var (ok, syncedPath, error) = await EnsureInventorySyncedAsync();
                if (!ok)
                {
                    HasPlan = false;
                    SummaryText = error ?? "Fix validation errors in Stage 1 (Inventory) before generating a plan.";
                    return;
                }
                targetFile = syncedPath;
                InventoryFilePath = syncedPath;
            }

            if (string.IsNullOrWhiteSpace(targetFile) || !await Task.Run(() => File.Exists(targetFile)))
            {
                HasPlan = false;
                SummaryText = "No inventory available. Add at least one switch card in Stage 1 first.";
                return;
            }

            var res = await _backend.CreatePlanAsync(targetFile);
            if (res.Success && res.Plan != null)
            {
                HasPlan = true;
                PlanJobId = res.Plan.JobId;
                Batches.Clear();
                Commands.Clear();

                for (int i = 0; i < res.Plan.Batches.Count; i++)
                {
                    bool isCanary = (i == 0);
                    Batches.Add(new BatchCardModel
                    {
                        BatchNumber = i + 1,
                        Title = isCanary ? $"Batch {i + 1} · Canary" : $"Batch {i + 1} · Production",
                        IsCanary = isCanary,
                        Devices = res.Plan.Batches[i],
                    });
                }

                foreach (var cmd in res.Plan.Commands)
                {
                    Commands.Add(cmd);
                }

                SummaryText = $"Approval plan ready for job {PlanJobId} — {Batches.Count} batch(es) isolated by redundancy group";
            }
            else
            {
                HasPlan = false;
                SummaryText = $"Plan creation failed: {res.Error}";
            }
        }
        catch (Exception ex)
        {
            HasPlan = false;
            SummaryText = $"Plan error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StateChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void ProceedToExecution()
    {
        NavigateToExecutionRequested?.Invoke(PlanJobId);
    }

    [RelayCommand]
    public void BackToInventory()
    {
        BackToInventoryRequested?.Invoke();
    }
}
