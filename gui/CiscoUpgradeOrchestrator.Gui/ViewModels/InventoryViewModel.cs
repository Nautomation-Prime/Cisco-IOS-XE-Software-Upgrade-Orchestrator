using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class DeviceCardViewModel : ViewModelBase
{
    public static readonly string[] SupportedPlatforms =
    {
        "catalyst9300",
        "catalyst9200",
        "cat9300",
        "cat9200",
        "c9300",
        "c9200"
    };

    private static readonly Regex Md5Regex = new("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);
    private bool _cachedDuplicateHostname;
    private bool _cachedDuplicateIp;

    public event Action<DeviceCardViewModel, bool>? CardModified;

    [ObservableProperty] private string _hostname = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _platform = "catalyst9300";
    [ObservableProperty] private string _currentVersion = string.Empty;
    [ObservableProperty] private string _targetVersion = string.Empty;
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private string _imageMd5 = string.Empty;
    [ObservableProperty] private string _redundancyGroup = string.Empty;
    [ObservableProperty] private string _sshPort = "22";
    [ObservableProperty] private bool _consoleVerified = true;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _status = "pending";

    // Per-field validation errors
    [ObservableProperty] private string _hostnameError = string.Empty;
    [ObservableProperty] private string _ipAddressError = string.Empty;
    [ObservableProperty] private string _platformError = string.Empty;
    [ObservableProperty] private string _targetVersionError = string.Empty;
    [ObservableProperty] private string _imagePathError = string.Empty;
    [ObservableProperty] private string _imageMd5Error = string.Empty;
    [ObservableProperty] private string _sshPortError = string.Empty;

    [ObservableProperty] private bool _isCardValid;
    [ObservableProperty] private string _cardSubtitle = string.Empty;

    public bool HasHostnameError => !string.IsNullOrEmpty(HostnameError);
    public bool HasIpAddressError => !string.IsNullOrEmpty(IpAddressError);
    public bool HasPlatformError => !string.IsNullOrEmpty(PlatformError);
    public bool HasTargetVersionError => !string.IsNullOrEmpty(TargetVersionError);
    public bool HasImagePathError => !string.IsNullOrEmpty(ImagePathError);
    public bool HasImageMd5Error => !string.IsNullOrEmpty(ImageMd5Error);
    public bool HasSshPortError => !string.IsNullOrEmpty(SshPortError);

    public string DisplayHostname => string.IsNullOrWhiteSpace(Hostname) ? "Unnamed switch" : Hostname.Trim();
    public string DisplayIp => string.IsNullOrWhiteSpace(IpAddress) ? "No IP set" : IpAddress.Trim();
    public string DisplayVersionTransition => string.IsNullOrWhiteSpace(CurrentVersion)
        ? (string.IsNullOrWhiteSpace(TargetVersion) ? "Target not set" : $"Target {TargetVersion.Trim()}")
        : $"{CurrentVersion.Trim()} → {(TargetVersion ?? string.Empty).Trim()}";
    public bool HasRedundancyGroup => !string.IsNullOrWhiteSpace(RedundancyGroup);

    public DeviceCardViewModel()
    {
        ValidateFields();
    }

    partial void OnHostnameChanged(string value)
    {
        if (value is null) { _hostname = string.Empty; }
        OnPropertyChanged(nameof(DisplayHostname));
        NotifyEdit(true);
    }

    partial void OnIpAddressChanged(string value)
    {
        if (value is null) { _ipAddress = string.Empty; }
        OnPropertyChanged(nameof(DisplayIp));
        NotifyEdit(true);
    }

    private string _lastValidPlatform = "catalyst9300";

    partial void OnPlatformChanging(string? oldValue, string newValue)
    {
        if (!string.IsNullOrWhiteSpace(oldValue))
        {
            _lastValidPlatform = oldValue;
        }
    }

    partial void OnPlatformChanged(string value)
    {
        // Avalonia ComboBox briefly writes null to SelectedItem when DataContext switches between cards.
        // Restore the last valid platform silently so switching cards never wipes Platform or throws NRE.
        if (string.IsNullOrWhiteSpace(value))
        {
            _platform = _lastValidPlatform;
            OnPropertyChanged(nameof(Platform));
            return;
        }
        _lastValidPlatform = value;
        NotifyEdit(false);
    }

    partial void OnCurrentVersionChanged(string value) { if (value is null) _currentVersion = string.Empty; OnPropertyChanged(nameof(DisplayVersionTransition)); NotifyEdit(false); }
    partial void OnTargetVersionChanged(string value) { if (value is null) _targetVersion = string.Empty; OnPropertyChanged(nameof(DisplayVersionTransition)); NotifyEdit(false); }
    partial void OnImagePathChanged(string value) { if (value is null) _imagePath = string.Empty; NotifyEdit(false); }
    partial void OnImageMd5Changed(string value) { if (value is null) _imageMd5 = string.Empty; NotifyEdit(false); }
    partial void OnRedundancyGroupChanged(string value) { if (value is null) _redundancyGroup = string.Empty; OnPropertyChanged(nameof(HasRedundancyGroup)); NotifyEdit(false); }
    partial void OnSshPortChanged(string value) { if (value is null) _sshPort = string.Empty; NotifyEdit(false); }
    partial void OnConsoleVerifiedChanged(bool value) => NotifyEdit(false);
    partial void OnEnabledChanged(bool value) => NotifyEdit(false);
    partial void OnNotesChanged(string value) { if (value is null) _notes = string.Empty; NotifyEdit(false); }

    private void NotifyEdit(bool affectsUniqueness)
    {
        if (CardModified != null)
        {
            CardModified.Invoke(this, affectsUniqueness);
        }
        else
        {
            ValidateFields();
        }
    }

    public List<string> ValidateFields(bool? duplicateHostname = null, bool? duplicateIp = null)
    {
        if (duplicateHostname.HasValue) _cachedDuplicateHostname = duplicateHostname.Value;
        if (duplicateIp.HasValue) _cachedDuplicateIp = duplicateIp.Value;

        bool isDupHost = _cachedDuplicateHostname;
        bool isDupIp = _cachedDuplicateIp;

        var errors = new List<string>();
        var label = DisplayHostname;

        // Hostname
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            HostnameError = "Hostname is required";
            errors.Add($"{label}: Missing required field 'hostname'");
        }
        else if (Hostname.Any(char.IsWhiteSpace))
        {
            HostnameError = "Hostname cannot contain spaces";
            errors.Add($"{label}: Hostname cannot contain whitespace");
        }
        else if (isDupHost)
        {
            HostnameError = "Duplicate hostname in inventory";
            errors.Add($"Duplicate hostname: {Hostname.Trim().ToLowerInvariant()}");
        }
        else
        {
            HostnameError = string.Empty;
        }

        // IP Address
        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            IpAddressError = "Management IP address is required";
            errors.Add($"{label}: Missing required field 'ip_address'");
        }
        else if (!IPAddress.TryParse(IpAddress.Trim(), out _))
        {
            IpAddressError = "Enter a valid IPv4 or IPv6 address (e.g., 192.168.1.10)";
            errors.Add($"{label}: IP address '{IpAddress.Trim()}' is invalid");
        }
        else if (isDupIp)
        {
            IpAddressError = "Duplicate management IP in inventory";
            errors.Add($"Duplicate management address: {IpAddress.Trim()}");
        }
        else
        {
            IpAddressError = string.Empty;
        }

        // Platform
        var normalizedPlatform = (Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedPlatform))
        {
            PlatformError = "Platform is required";
            errors.Add($"{label}: Missing required field 'platform'");
        }
        else if (!SupportedPlatforms.Contains(normalizedPlatform))
        {
            PlatformError = "Must be catalyst9200, catalyst9300, cat9200, cat9300, c9200, or c9300";
            errors.Add($"{label}: Platform '{normalizedPlatform}' is not supported");
        }
        else
        {
            PlatformError = string.Empty;
        }

        // Target Version
        if (string.IsNullOrWhiteSpace(TargetVersion))
        {
            TargetVersionError = "Target IOS-XE version is required (e.g., 17.12.1)";
            errors.Add($"{label}: Missing required field 'target_version'");
        }
        else
        {
            TargetVersionError = string.Empty;
        }

        // Image Path
        if (string.IsNullOrWhiteSpace(ImagePath))
        {
            ImagePathError = "IOS-XE image path is required";
            errors.Add($"{label}: Missing required field 'image_path'");
        }
        else
        {
            ImagePathError = string.Empty;
        }

        // Image MD5
        var md5 = (ImageMd5 ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(md5))
        {
            ImageMd5Error = "32-character MD5 checksum is required";
            errors.Add($"{label}: Missing required field 'image_md5'");
        }
        else if (!Md5Regex.IsMatch(md5))
        {
            ImageMd5Error = $"MD5 must be exactly 32 hex characters ({md5.Length}/32)";
            errors.Add($"{label}: image_md5 must contain exactly 32 hexadecimal characters");
        }
        else
        {
            ImageMd5Error = string.Empty;
        }

        // SSH Port
        var portText = (SshPort ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(portText))
        {
            if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
            {
                SshPortError = "SSH port must be between 1 and 65535";
                errors.Add($"{label}: ssh_port '{portText}' must be between 1 and 65535");
            }
            else
            {
                SshPortError = string.Empty;
            }
        }
        else
        {
            SshPortError = string.Empty;
        }

        OnPropertyChanged(nameof(HasHostnameError));
        OnPropertyChanged(nameof(HasIpAddressError));
        OnPropertyChanged(nameof(HasPlatformError));
        OnPropertyChanged(nameof(HasTargetVersionError));
        OnPropertyChanged(nameof(HasImagePathError));
        OnPropertyChanged(nameof(HasImageMd5Error));
        OnPropertyChanged(nameof(HasSshPortError));

        IsCardValid = errors.Count == 0;
        CardSubtitle = IsCardValid
            ? $"{Platform} · {DisplayVersionTransition}"
            : $"{errors.Count} field issue(s) to fix";

        return errors;
    }

    public static DeviceCardViewModel FromDeviceModel(DeviceModel m)
    {
        var card = new DeviceCardViewModel
        {
            Hostname = m.Hostname ?? string.Empty,
            IpAddress = m.IpAddress ?? string.Empty,
            Platform = string.IsNullOrWhiteSpace(m.Platform) ? "catalyst9300" : m.Platform,
            CurrentVersion = m.CurrentVersion ?? string.Empty,
            TargetVersion = m.TargetVersion ?? string.Empty,
            ImagePath = m.ImagePath ?? string.Empty,
            ImageMd5 = m.ImageMd5 ?? string.Empty,
            RedundancyGroup = m.RedundancyGroup ?? string.Empty,
            SshPort = string.IsNullOrWhiteSpace(m.SshPort) ? "22" : m.SshPort,
            ConsoleVerified = !string.Equals(m.ConsoleVerified, "No", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(m.ConsoleVerified, "false", StringComparison.OrdinalIgnoreCase),
            Enabled = !string.Equals(m.Enabled, "No", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(m.Enabled, "false", StringComparison.OrdinalIgnoreCase),
            Notes = m.Notes ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(m.Status) ? "pending" : m.Status
        };
        card.ValidateFields();
        return card;
    }

    public DeviceModel ToDeviceModel()
    {
        return new DeviceModel
        {
            Hostname = (Hostname ?? string.Empty).Trim(),
            IpAddress = (IpAddress ?? string.Empty).Trim(),
            Platform = string.IsNullOrWhiteSpace(Platform) ? "catalyst9300" : Platform.Trim().ToLowerInvariant(),
            CurrentVersion = string.IsNullOrWhiteSpace(CurrentVersion) ? null : CurrentVersion.Trim(),
            TargetVersion = (TargetVersion ?? string.Empty).Trim(),
            ImagePath = (ImagePath ?? string.Empty).Trim(),
            ImageMd5 = (ImageMd5 ?? string.Empty).Trim().ToLowerInvariant(),
            RedundancyGroup = string.IsNullOrWhiteSpace(RedundancyGroup) ? null : RedundancyGroup.Trim(),
            SshPort = string.IsNullOrWhiteSpace(SshPort) ? null : SshPort.Trim(),
            ConsoleVerified = ConsoleVerified ? "Yes" : "No",
            Enabled = Enabled ? "Yes" : "No",
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Status = string.IsNullOrWhiteSpace(Status) ? "pending" : Status.Trim()
        };
    }
}

public partial class InventoryViewModel : ViewModelBase
{
    private readonly PythonBackendService _backend;
    private bool _suppressCardSync;

    public string[] SupportedPlatforms => DeviceCardViewModel.SupportedPlatforms;

    [ObservableProperty]
    private string _inventoryFilePath = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isComputingMd5;

    [ObservableProperty]
    private double _md5Progress;

    [ObservableProperty]
    private bool _hasValidationResult;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string _validationSummary = string.Empty;

    [ObservableProperty]
    private string _filterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DeviceModel> _allDevices = new();

    [ObservableProperty]
    private ObservableCollection<DeviceModel> _filteredDevices = new();

    [ObservableProperty]
    private ObservableCollection<DeviceCardViewModel> _deviceCards = new();

    [ObservableProperty]
    private ObservableCollection<DeviceCardViewModel> _filteredCards = new();

    [ObservableProperty]
    private DeviceCardViewModel? _selectedCard;

    [ObservableProperty]
    private ObservableCollection<string> _validationErrors = new();

    public bool HasDevices => DeviceCards.Count > 0 || AllDevices.Count > 0;
    public bool HasSelectedCard => SelectedCard != null;
    public int DeviceCount => DeviceCards.Count > 0 ? DeviceCards.Count : AllDevices.Count;
    public int ValidCardCount => DeviceCards.Count(c => c.IsCardValid);
    public int ErrorCount => ValidationErrors.Count;
    public string InventoryFileDisplayName => string.IsNullOrWhiteSpace(InventoryFilePath)
        ? "gui_inventory.xlsx"
        : Path.GetFileName(InventoryFilePath);

    public Action<string>? StatusMessageRequested;
    public Action<string>? ContinueToPlanRequested;
    public Action? OpenConfigRequested;
    public event Action? StateChanged;

    public InventoryViewModel()
    {
        _backend = PythonBackendService.Instance;

        var managedPath = Path.Combine(_backend.ProjectRoot, "inventory", "gui_inventory.xlsx");
        InventoryFilePath = managedPath;

        // Only auto-load the user's saved GUI inventory (never auto-load starter template mock rows)
        if (File.Exists(managedPath))
        {
            _ = LoadInventoryAsync();
        }
    }

    partial void OnFilterQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnInventoryFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(InventoryFileDisplayName));
        StateChanged?.Invoke();
    }

    partial void OnSelectedCardChanged(DeviceCardViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCard));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasSelectedCard));
        OnPropertyChanged(nameof(DeviceCount));
        OnPropertyChanged(nameof(ValidCardCount));
        OnPropertyChanged(nameof(ErrorCount));
        StateChanged?.Invoke();
    }

    private void AttachCard(DeviceCardViewModel card)
    {
        card.CardModified += OnAnyCardModified;
        DeviceCards.Add(card);
    }

    private void OnAnyCardModified(DeviceCardViewModel changedCard, bool affectsUniqueness)
    {
        if (_suppressCardSync) return;
        IsDirty = true;

        if (affectsUniqueness)
        {
            ValidateAllCards();
        }
        else
        {
            changedCard.ValidateFields();
            UpdateValidationSummaryFast();
        }

        // Only re-filter when an active search filter is set; otherwise cards update in-place at 60 FPS
        if (!string.IsNullOrWhiteSpace(FilterQuery))
        {
            SyncFilteredCardsInPlace();
        }
    }

    private void UpdateValidationSummaryFast()
    {
        int invalidCount = DeviceCards.Count(c => !c.IsCardValid);
        if (invalidCount == 0 && DeviceCards.Count > 0)
        {
            ValidationErrors.Clear();
            IsValid = true;
            HasValidationResult = true;
            ValidationSummary = $"All {DeviceCards.Count} switch card(s) validated — ready for planning";
            NotifyCounts();
        }
        else
        {
            // Collect exact errors cleanly
            ValidateAllCards();
        }
    }

    private void SyncFilteredCardsInPlace()
    {
        var q = (FilterQuery ?? string.Empty).Trim().ToLowerInvariant();
        var matching = DeviceCards.Where(c =>
            (c.Hostname ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.IpAddress ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.Platform ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.RedundancyGroup ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.TargetVersion ?? string.Empty).ToLowerInvariant().Contains(q)
        ).ToList();

        // Only mutate collection if membership actually changed
        if (matching.Count == FilteredCards.Count && matching.SequenceEqual(FilteredCards))
        {
            return;
        }

        FilteredCards.Clear();
        foreach (var item in matching)
        {
            FilteredCards.Add(item);
        }
    }

    private void SyncModelsFromCards()
    {
        AllDevices.Clear();
        foreach (var card in DeviceCards)
        {
            AllDevices.Add(card.ToDeviceModel());
        }
    }

    public bool ValidateAllCards()
    {
        ValidationErrors.Clear();

        if (DeviceCards.Count == 0)
        {
            IsValid = false;
            HasValidationResult = true;
            ValidationSummary = "Add at least one switch card or import an Excel inventory to begin";
            NotifyCounts();
            return false;
        }

        // Detect duplicate hostnames & normalized IPs across cards
        var hostCounts = DeviceCards
            .Where(c => !string.IsNullOrWhiteSpace(c.Hostname))
            .GroupBy(c => (c.Hostname ?? string.Empty).Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        var ipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in DeviceCards)
        {
            if (IPAddress.TryParse(c.IpAddress?.Trim(), out var parsedIp))
            {
                var norm = parsedIp.ToString();
                ipCounts[norm] = ipCounts.TryGetValue(norm, out var cnt) ? cnt + 1 : 1;
            }
        }

        var allErrors = new List<string>();
        foreach (var card in DeviceCards)
        {
            var hostKey = (card.Hostname ?? string.Empty).Trim().ToLowerInvariant();
            bool dupHost = !string.IsNullOrEmpty(hostKey) && hostCounts.TryGetValue(hostKey, out var hc) && hc > 1;

            bool dupIp = false;
            if (IPAddress.TryParse(card.IpAddress?.Trim(), out var parsedIp))
            {
                dupIp = ipCounts.TryGetValue(parsedIp.ToString(), out var ic) && ic > 1;
            }

            var cardErrors = card.ValidateFields(dupHost, dupIp);
            foreach (var err in cardErrors)
            {
                if (!allErrors.Contains(err))
                {
                    allErrors.Add(err);
                }
            }
        }

        foreach (var err in allErrors)
        {
            ValidationErrors.Add(err);
        }

        IsValid = allErrors.Count == 0;
        HasValidationResult = true;
        ValidationSummary = IsValid
            ? $"All {DeviceCards.Count} switch card(s) validated — ready for planning"
            : $"{allErrors.Count} validation issue(s) across {DeviceCards.Count(c => !c.IsCardValid)} switch card(s)";

        NotifyCounts();
        return IsValid;
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterQuery))
        {
            FilteredDevices = new ObservableCollection<DeviceModel>(AllDevices);
            FilteredCards = new ObservableCollection<DeviceCardViewModel>(DeviceCards);
            NotifyCounts();
            return;
        }

        var q = FilterQuery.Trim().ToLowerInvariant();

        var deviceMatches = AllDevices.Where(d =>
            (d.Hostname ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (d.IpAddress ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (d.Platform ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (d.RedundancyGroup ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (d.TargetVersion ?? string.Empty).ToLowerInvariant().Contains(q)
        ).ToList();

        var cardMatches = DeviceCards.Where(c =>
            (c.Hostname ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.IpAddress ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.Platform ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.RedundancyGroup ?? string.Empty).ToLowerInvariant().Contains(q) ||
            (c.TargetVersion ?? string.Empty).ToLowerInvariant().Contains(q)
        ).ToList();

        FilteredDevices = new ObservableCollection<DeviceModel>(deviceMatches);
        FilteredCards = new ObservableCollection<DeviceCardViewModel>(cardMatches);
        NotifyCounts();
    }

    [RelayCommand]
    public void AddSwitch()
    {
        _suppressCardSync = true;
        var nextIndex = DeviceCards.Count + 1;
        var card = new DeviceCardViewModel
        {
            Hostname = $"SW-ACCESS-{nextIndex:D2}",
            IpAddress = string.Empty,
            Platform = "catalyst9300",
            TargetVersion = string.Empty,
            ImagePath = string.Empty,
            ImageMd5 = string.Empty,
            SshPort = "22",
            ConsoleVerified = true,
            Enabled = true
        };
        AttachCard(card);
        SelectedCard = card;
        _suppressCardSync = false;
        IsDirty = true;

        ValidateAllCards();
        SyncModelsFromCards();
        ApplyFilter();
    }

    [RelayCommand]
    public void DuplicateSelectedSwitch()
    {
        if (SelectedCard == null) return;

        _suppressCardSync = true;
        var nextIndex = DeviceCards.Count + 1;
        var baseName = string.IsNullOrWhiteSpace(SelectedCard.Hostname)
            ? $"SW-ACCESS-{nextIndex:D2}"
            : $"{SelectedCard.Hostname.Trim()}-COPY-{nextIndex}";
        var clone = new DeviceCardViewModel
        {
            Hostname = baseName,
            IpAddress = string.Empty,
            Platform = string.IsNullOrWhiteSpace(SelectedCard.Platform) ? "catalyst9300" : SelectedCard.Platform,
            CurrentVersion = SelectedCard.CurrentVersion ?? string.Empty,
            TargetVersion = SelectedCard.TargetVersion ?? string.Empty,
            ImagePath = SelectedCard.ImagePath ?? string.Empty,
            ImageMd5 = SelectedCard.ImageMd5 ?? string.Empty,
            RedundancyGroup = SelectedCard.RedundancyGroup ?? string.Empty,
            SshPort = SelectedCard.SshPort ?? "22",
            ConsoleVerified = SelectedCard.ConsoleVerified,
            Enabled = SelectedCard.Enabled,
            Notes = SelectedCard.Notes ?? string.Empty
        };
        AttachCard(clone);
        SelectedCard = clone;
        _suppressCardSync = false;
        IsDirty = true;

        ValidateAllCards();
        SyncModelsFromCards();
        ApplyFilter();
    }

    [RelayCommand]
    public void RemoveSwitch(DeviceCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target == null) return;

        target.CardModified -= OnAnyCardModified;
        var idx = DeviceCards.IndexOf(target);
        DeviceCards.Remove(target);
        IsDirty = true;

        if (SelectedCard == target)
        {
            SelectedCard = DeviceCards.Count > 0
                ? DeviceCards[Math.Clamp(idx, 0, DeviceCards.Count - 1)]
                : null;
        }

        ValidateAllCards();
        SyncModelsFromCards();
        ApplyFilter();
    }

    [RelayCommand]
    public async Task ComputeSelectedMd5Async()
    {
        if (IsComputingMd5) return;
        var targetCard = SelectedCard;
        if (targetCard == null) return;

        var path = (targetCard.ImagePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ValidationSummary = "Select a local .bin image file first to compute its MD5 checksum";
            HasValidationResult = true;
            return;
        }

        IsComputingMd5 = true;
        Md5Progress = 0;
        ValidationSummary = $"Computing MD5 for {Path.GetFileName(path)}...";
        HasValidationResult = true;

        try
        {
            var result = await Task.Run(() =>
            {
                // Run File.Exists & 1 MB sequential-scan streaming hash completely off the UI thread
                if (!File.Exists(path))
                {
                    return (found: false, hash: string.Empty);
                }

                const int bufferSize = 1024 * 1024; // 1 MB sequential buffer
                var buffer = new byte[bufferSize];
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize,
                    FileOptions.SequentialScan);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

                long totalLength = stream.Length;
                long totalRead = 0;
                int lastReportedPct = -1;
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.AppendData(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalLength > 0)
                    {
                        int pct = (int)((totalRead * 100L) / totalLength);
                        if (pct != lastReportedPct)
                        {
                            lastReportedPct = pct;
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => Md5Progress = pct);
                        }
                    }
                }

                var digest = hasher.GetHashAndReset();
                return (found: true, hash: Convert.ToHexString(digest).ToLowerInvariant());
            });

            if (!result.found)
            {
                ValidationSummary = "Image file not found on disk — verify the .bin path";
                HasValidationResult = true;
                return;
            }

            targetCard.ImageMd5 = result.hash;
            Md5Progress = 100;
            ValidationSummary = $"Computed MD5 for {Path.GetFileName(path)}: {result.hash}";
            HasValidationResult = true;
        }
        catch (Exception ex)
        {
            ValidationSummary = $"MD5 calculation failed: {ex.Message}";
            HasValidationResult = true;
        }
        finally
        {
            IsComputingMd5 = false;
        }
    }

    [RelayCommand]
    public async Task LoadInventoryAsync()
    {
        var path = (InventoryFilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path) || !await Task.Run(() => File.Exists(path)))
        {
            ValidationSummary = "Select an existing Excel (.xlsx) file to import, or add switches directly below";
            HasValidationResult = true;
            IsValid = false;
            NotifyCounts();
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _backend.ParseInventoryAsync(path);
            _suppressCardSync = true;
            foreach (var existing in DeviceCards) existing.CardModified -= OnAnyCardModified;
            DeviceCards.Clear();
            AllDevices.Clear();
            ValidationErrors.Clear();

            foreach (var dev in result.Devices)
            {
                AllDevices.Add(dev);
                AttachCard(DeviceCardViewModel.FromDeviceModel(dev));
            }
            SelectedCard = DeviceCards.FirstOrDefault();
            _suppressCardSync = false;
            IsDirty = false;

            ValidateAllCards();

            // Merge any backend-specific errors not already caught locally
            foreach (var err in result.Errors)
            {
                if (!ValidationErrors.Contains(err))
                {
                    ValidationErrors.Add(err);
                }
            }

            IsValid = result.Valid && ValidationErrors.Count == 0;
            HasValidationResult = true;
            ValidationSummary = IsValid
                ? $"Imported and validated {DeviceCards.Count} switch card(s) from {Path.GetFileName(InventoryFilePath)}"
                : $"Imported with {ValidationErrors.Count} validation issue(s) — fix highlighted fields below";

            ApplyFilter();
        }
        catch (Exception ex)
        {
            IsValid = false;
            HasValidationResult = true;
            ValidationSummary = $"Import error: {ex.Message}";
            NotifyCounts();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task<bool> SaveInventoryAsync()
    {
        if (!ValidateAllCards())
        {
            return false;
        }

        // Protect the starter inventory_template.xlsx — save GUI-edited cards to managed gui_inventory.xlsx
        var managedPath = Path.Combine(_backend.ProjectRoot, "inventory", "gui_inventory.xlsx");
        var targetPath = string.IsNullOrWhiteSpace(InventoryFilePath) ||
                         InventoryFilePath.EndsWith("inventory_template.xlsx", StringComparison.OrdinalIgnoreCase)
            ? managedPath
            : InventoryFilePath;

        IsLoading = true;
        try
        {
            SyncModelsFromCards();
            var res = await _backend.SaveInventoryAsync(targetPath, AllDevices);
            InventoryFilePath = string.IsNullOrWhiteSpace(res.FilePath) ? targetPath : res.FilePath;

            ValidationErrors.Clear();
            foreach (var err in res.Errors)
            {
                ValidationErrors.Add(err);
            }

            IsValid = res.Success && res.Valid;
            if (IsValid) IsDirty = false;
            HasValidationResult = true;
            ValidationSummary = IsValid
                ? $"Saved and verified {res.TotalDevices} switch(es) to {Path.GetFileName(InventoryFilePath)}"
                : $"Save validation failed: {string.Join("; ", res.Errors)}";

            NotifyCounts();
            return IsValid;
        }
        catch (Exception ex)
        {
            IsValid = false;
            HasValidationResult = true;
            ValidationSummary = $"Save error: {ex.Message}";
            NotifyCounts();
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Ensures Stage 1's switch cards are validated and synced to an .xlsx workbook
    /// before Stage 2 (Plan) or Stage 3 (Execute) consumes the inventory.
    /// </summary>
    public async Task<(bool ok, string path, string? error)> EnsureSyncedForPipelineAsync()
    {
        if (DeviceCards.Count > 0)
        {
            if (!ValidateAllCards())
            {
                return (false, InventoryFilePath, $"Stage 1 has {ValidationErrors.Count} validation issue(s). Return to Inventory to fix highlighted fields.");
            }

            bool needsSave = IsDirty
                || string.IsNullOrWhiteSpace(InventoryFilePath)
                || !File.Exists(InventoryFilePath)
                || InventoryFilePath.EndsWith("inventory_template.xlsx", StringComparison.OrdinalIgnoreCase);

            if (needsSave)
            {
                var saved = await SaveInventoryAsync();
                if (!saved)
                {
                    return (false, InventoryFilePath, ValidationSummary);
                }
            }

            return (true, InventoryFilePath, null);
        }

        if (!string.IsNullOrWhiteSpace(InventoryFilePath) && File.Exists(InventoryFilePath))
        {
            return (true, InventoryFilePath, null);
        }

        return (false, string.Empty, "No switches configured in Stage 1 (Inventory). Add at least one switch card first.");
    }

    [RelayCommand]
    public async Task CreateTemplateAsync()
    {
        IsLoading = true;
        try
        {
            var target = Path.Combine(_backend.ProjectRoot, "inventory", "inventory_template.xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var tcs = new TaskCompletionSource<bool>();
            await _backend.RunOrchestratorStreamingAsync(
                $"create-template -o \"{target}\"",
                line => { },
                (ok, code) => tcs.SetResult(ok),
                default
            );

            await tcs.Task;
            InventoryFilePath = target;
            await LoadInventoryAsync();
            StatusMessageRequested?.Invoke($"Template created at {target}");
        }
        catch (Exception ex)
        {
            ValidationSummary = $"Failed to create template: {ex.Message}";
            HasValidationResult = true;
            IsValid = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ExportReportAsync()
    {
        if (DeviceCards.Count > 0 && IsValid)
        {
            await SaveInventoryAsync();
        }

        if (string.IsNullOrWhiteSpace(InventoryFilePath) || !File.Exists(InventoryFilePath))
        {
            ValidationSummary = "Save or import an inventory before exporting a summary report";
            HasValidationResult = true;
            return;
        }

        IsLoading = true;
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            await _backend.RunOrchestratorStreamingAsync(
                $"report -f \"{InventoryFilePath}\"",
                line => { },
                (ok, code) => tcs.SetResult(ok),
                default
            );

            await tcs.Task;
            ValidationSummary = "Inventory summary report exported to reports directory";
            HasValidationResult = true;
            StatusMessageRequested?.Invoke("Inventory summary report exported successfully.");
        }
        catch (Exception ex)
        {
            ValidationSummary = $"Report export error: {ex.Message}";
            HasValidationResult = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ContinueToPlanAsync()
    {
        if (DeviceCards.Count > 0)
        {
            var saved = await SaveInventoryAsync();
            if (!saved) return;
        }

        ContinueToPlanRequested?.Invoke(InventoryFilePath);
    }

    [RelayCommand]
    public void OpenConfig()
    {
        OpenConfigRequested?.Invoke();
    }
}
