using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly PythonBackendService _backend;

    [ObservableProperty]
    private ObservableCollection<JobModel> _jobs = new();

    [ObservableProperty]
    private ObservableCollection<JobModel> _filteredJobs = new();

    [ObservableProperty]
    private JobModel? _selectedJob;

    [ObservableProperty]
    private ObservableCollection<DeviceModel> _selectedJobDevices = new();

    [ObservableProperty]
    private ObservableCollection<JobEventModel> _selectedJobEvents = new();

    [ObservableProperty]
    private string _jobSummaryText = "Select a job to inspect device states or run lease actions";

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private int _totalJobs;

    [ObservableProperty]
    private int _plannedJobs;

    [ObservableProperty]
    private int _completedJobs;

    [ObservableProperty]
    private int _failedJobs;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _actionSucceeded = true;

    [ObservableProperty]
    private bool _isConfirmingClearHistory;

    public bool HasSelectedJob => SelectedJob != null;
    public bool HasJobs => Jobs.Count > 0;
    public bool HasActionMessage => !string.IsNullOrWhiteSpace(ActionMessage);
    public bool IsFilterAll => SelectedFilter == "All";
    public bool IsFilterPlanned => SelectedFilter == "Planned";
    public bool IsFilterCompleted => SelectedFilter == "Completed";
    public bool IsFilterFailed => SelectedFilter == "Failed";

    public Action<string>? RequestExecution;
    public Action? BackToExecutionRequested;
    public event Action? StateChanged;

    public HistoryViewModel()
    {
        _backend = PythonBackendService.Instance;
        // Loaded lazily when Stage 4 is opened or after execution finishes (eliminates startup lag)
    }

    partial void OnActionMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasActionMessage));
    }

    partial void OnSelectedJobChanged(JobModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedJob));
        if (value != null)
        {
            _ = LoadJobDetailsAsync(value.JobId);
        }
        else
        {
            SelectedJobDevices.Clear();
            SelectedJobEvents.Clear();
            JobSummaryText = "Select a job to inspect device states or run lease actions";
        }
    }

    partial void OnSelectedFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterPlanned));
        OnPropertyChanged(nameof(IsFilterCompleted));
        OnPropertyChanged(nameof(IsFilterFailed));
        ApplyFilter();
    }

    [RelayCommand]
    public void FilterJobs(string filter)
    {
        var normalized = string.IsNullOrWhiteSpace(filter) ? "All" : filter;
        if (SelectedFilter == normalized)
        {
            ApplyFilter();
        }
        else
        {
            SelectedFilter = normalized;
        }
    }

    public static bool IsPlannedOrActiveStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s is "planned" or "created" or "running" or "paused";
    }

    public static bool IsCompletedOrStagedStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s is "completed" or "complete" or "staged" or "awaiting_cleanup";
    }

    public static bool IsFailedOrHaltedStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s.Contains("fail") || s.Contains("halt") || s.Contains("abort") || s.Contains("cancel");
    }

    private void ApplyFilter()
    {
        var list = SelectedFilter switch
        {
            "Planned" => Jobs.Where(j => IsPlannedOrActiveStatus(j.Status)).ToList(),
            "Completed" => Jobs.Where(j => IsCompletedOrStagedStatus(j.Status)).ToList(),
            "Failed" => Jobs.Where(j => IsFailedOrHaltedStatus(j.Status)).ToList(),
            _ => Jobs.ToList()
        };

        FilteredJobs = new ObservableCollection<JobModel>(list);
    }

    private void RecalculateCounts()
    {
        TotalJobs = Jobs.Count;
        PlannedJobs = Jobs.Count(j => IsPlannedOrActiveStatus(j.Status));
        CompletedJobs = Jobs.Count(j => IsCompletedOrStagedStatus(j.Status));
        FailedJobs = Jobs.Count(j => IsFailedOrHaltedStatus(j.Status));
        OnPropertyChanged(nameof(HasJobs));
        StateChanged?.Invoke();
    }

    [RelayCommand]
    public async Task LoadJobsAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var jobsList = await _backend.GetJobsAsync();
            var previousId = SelectedJob?.JobId;

            Jobs.Clear();
            foreach (var j in jobsList)
            {
                Jobs.Add(j);
            }

            RecalculateCounts();
            ApplyFilter();

            if (!string.IsNullOrEmpty(previousId))
            {
                SelectedJob = FilteredJobs.FirstOrDefault(j => j.JobId == previousId);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadJobDetailsAsync(string jobId)
    {
        try
        {
            var details = await _backend.GetJobDetailsAsync(jobId);
            SelectedJobDevices.Clear();
            SelectedJobEvents.Clear();

            foreach (var dev in details.Devices)
            {
                SelectedJobDevices.Add(dev);
            }

            foreach (var ev in details.Events)
            {
                SelectedJobEvents.Add(ev);
            }

            JobSummaryText = $"Job {jobId} · Total: {details.Summary.Total} · Complete: {details.Summary.Complete} · Failed: {details.Summary.Failed} · Cleanup pending: {details.Summary.CleanupPending}";
        }
        catch (Exception ex)
        {
            JobSummaryText = $"Failed to load job details: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CancelJobLeaseAsync()
    {
        if (SelectedJob == null) return;
        var jobId = SelectedJob.JobId;
        IsLoading = true;
        ActionMessage = $"Cancelling job {jobId} and releasing device leases...";
        ActionSucceeded = true;

        try
        {
            var output = new StringBuilder();
            var tcs = new TaskCompletionSource<(bool ok, int code)>();

            // --yes is mandatory because orchestrator.py cancel prompts via click.confirm() otherwise
            await _backend.RunOrchestratorStreamingAsync(
                $"cancel --job-id \"{jobId}\" --yes",
                line =>
                {
                    if (!string.IsNullOrWhiteSpace(line)) output.AppendLine(line);
                },
                (ok, code) => tcs.TrySetResult((ok, code)),
                default
            );

            var (ok, code) = await tcs.Task;
            var lastLine = output.ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? string.Empty;

            ActionSucceeded = ok;
            ActionMessage = ok
                ? (string.IsNullOrWhiteSpace(lastLine) ? $"Cancelled job {jobId} and released reservations" : lastLine)
                : $"Cancel failed (exit {code}): {lastLine}";
        }
        finally
        {
            IsLoading = false;
        }

        await LoadJobsAsync();
    }

    [RelayCommand]
    public async Task CleanupPackagesAsync()
    {
        if (SelectedJob == null) return;
        var jobId = SelectedJob.JobId;
        IsLoading = true;
        ActionMessage = $"Running inactive package cleanup for job {jobId}...";
        ActionSucceeded = true;

        try
        {
            var output = new StringBuilder();
            var tcs = new TaskCompletionSource<(bool ok, int code)>();

            await _backend.RunOrchestratorStreamingAsync(
                $"cleanup --job-id \"{jobId}\" --yes",
                line =>
                {
                    if (!string.IsNullOrWhiteSpace(line)) output.AppendLine(line);
                },
                (ok, code) => tcs.TrySetResult((ok, code)),
                default
            );

            var (ok, code) = await tcs.Task;
            var lastLine = output.ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? string.Empty;

            ActionSucceeded = ok;
            ActionMessage = ok
                ? $"Inactive package cleanup finished for job {jobId}"
                : $"Cleanup failed (exit {code}): {lastLine}";
        }
        finally
        {
            IsLoading = false;
        }

        await LoadJobsAsync();
    }

    [RelayCommand]
    public async Task DeleteSelectedJobAsync()
    {
        if (SelectedJob == null || IsLoading) return;
        var jobId = SelectedJob.JobId;
        IsLoading = true;
        ActionMessage = $"Deleting job {jobId} from StateStore...";
        ActionSucceeded = true;

        try
        {
            var res = await _backend.DeleteJobAsync(jobId);
            ActionSucceeded = res.Success;
            ActionMessage = res.Success
                ? (res.Message ?? $"Deleted job {jobId}")
                : (res.Error ?? $"Failed to delete job {jobId}");

            if (res.Success)
            {
                SelectedJob = null;
            }
        }
        finally
        {
            IsLoading = false;
        }

        await LoadJobsAsync();
    }

    [RelayCommand]
    public void RequestClearHistory()
    {
        if (!HasJobs || IsLoading) return;
        IsConfirmingClearHistory = true;
    }

    [RelayCommand]
    public void CancelClearHistory()
    {
        IsConfirmingClearHistory = false;
    }

    [RelayCommand]
    public async Task ConfirmClearHistoryAsync()
    {
        if (IsLoading) return;
        IsConfirmingClearHistory = false;
        IsLoading = true;
        ActionMessage = "Clearing inactive jobs from StateStore...";
        ActionSucceeded = true;

        try
        {
            var res = await _backend.ClearHistoryAsync();
            ActionSucceeded = res.Success;
            ActionMessage = res.Success
                ? (res.Message ?? $"Cleared {res.Deleted} job(s)")
                : (res.Error ?? "Failed to clear history");

            if (res.Success)
            {
                SelectedJob = null;
            }
        }
        finally
        {
            IsLoading = false;
        }

        await LoadJobsAsync();
    }

    [RelayCommand]
    public void ResumeJob()
    {
        if (SelectedJob != null)
        {
            RequestExecution?.Invoke(SelectedJob.JobId);
        }
    }

    [RelayCommand]
    public void BackToExecution()
    {
        BackToExecutionRequested?.Invoke();
    }
}
