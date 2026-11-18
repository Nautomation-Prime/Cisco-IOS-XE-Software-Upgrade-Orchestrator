using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly PythonBackendService _backend;

    [ObservableProperty]
    private int _totalJobs;

    [ObservableProperty]
    private int _plannedJobs;

    [ObservableProperty]
    private int _completedJobs;

    [ObservableProperty]
    private int _failedJobs;

    [ObservableProperty]
    private string _pythonStatus = "Checking Python environment...";

    [ObservableProperty]
    private bool _isPythonReady;

    [ObservableProperty]
    private string _activeJobBanner = "No active job lock detected. Safe to orchestrate.";

    [ObservableProperty]
    private ObservableCollection<JobModel> _recentJobs = new();

    public Action<string>? NavigateRequested;

    public DashboardViewModel()
    {
        _backend = PythonBackendService.Instance;
        _ = LoadDashboardDataAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadDashboardDataAsync();
    }

    [RelayCommand]
    public void GoToInventory() => NavigateRequested?.Invoke("Inventory");

    [RelayCommand]
    public void GoToPlanner() => NavigateRequested?.Invoke("Planner");

    [RelayCommand]
    public void GoToExecution() => NavigateRequested?.Invoke("Execution");

    [RelayCommand]
    public void GoToHistory() => NavigateRequested?.Invoke("History");

    public async Task LoadDashboardDataAsync()
    {
        // 1. Check Python
        var env = await _backend.TestEnvironmentAsync();
        if (env != null && env.AllInstalled)
        {
            IsPythonReady = true;
            PythonStatus = $"Python Ready ({env.PythonVersion.Split(' ')[0]}) • All dependencies active";
        }
        else if (env != null)
        {
            IsPythonReady = false;
            PythonStatus = "Python found but some dependencies missing.";
        }
        else
        {
            IsPythonReady = false;
            PythonStatus = "Python interpreter not found. Please check Settings.";
        }

        // 2. Load Jobs
        var jobs = await _backend.GetJobsAsync();
        RecentJobs.Clear();
        int planned = 0, completed = 0, failed = 0;

        foreach (var job in jobs)
        {
            if (RecentJobs.Count < 6)
            {
                RecentJobs.Add(job);
            }

            if (job.Status == "planned") planned++;
            else if (job.Status == "completed" || job.Status == "complete") completed++;
            else if (job.Status.Contains("fail")) failed++;
        }

        TotalJobs = jobs.Count;
        PlannedJobs = planned;
        CompletedJobs = completed;
        FailedJobs = failed;
    }
}
