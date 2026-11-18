using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;

namespace CiscoUpgradeOrchestrator.Gui.ViewModels;

public partial class CredentialsViewModel : ViewModelBase
{
    private readonly CredentialStore _store;

    [ObservableProperty] private string _vaultPassword = string.Empty;
    [ObservableProperty] private string _vaultConfirmation = string.Empty;
    [ObservableProperty] private bool _isVaultUnlocked;
    [ObservableProperty] private bool _isVaultBusy;
    [ObservableProperty] private bool _needsVaultCreation;
    [ObservableProperty] private string _vaultStatus = "Vault locked — unlock or create an encrypted vault to store SSH presets";
    [ObservableProperty] private bool _hasVaultError;

    // Preset form inputs + real-time validation
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private string _newUsername = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _newSecret = string.Empty;

    [ObservableProperty] private string _profileNameError = string.Empty;
    [ObservableProperty] private string _usernameError = string.Empty;
    [ObservableProperty] private string _passwordError = string.Empty;
    [ObservableProperty] private string _presetFeedback = string.Empty;

    [ObservableProperty] private ObservableCollection<CredentialEntry> _storedCredentials = new();

    public bool HasStoredCredentials => StoredCredentials.Count > 0;
    public bool HasProfileNameError => !string.IsNullOrEmpty(ProfileNameError);
    public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);
    public bool HasPresetFeedback => !string.IsNullOrEmpty(PresetFeedback);
    public string UnlockButtonText => NeedsVaultCreation ? "Create encrypted vault" : "Unlock vault";

    public event Action<CredentialEntry>? CredentialAppliedToSession;
    public event Action? VaultChanged;

    public CredentialsViewModel(CredentialStore? store = null)
    {
        _store = store ?? new CredentialStore();
        RefreshVaultState();
    }

    partial void OnNeedsVaultCreationChanged(bool value)
    {
        OnPropertyChanged(nameof(UnlockButtonText));
    }

    partial void OnNewProfileNameChanged(string value)
    {
        if (value is null) _newProfileName = string.Empty;
        if (HasProfileNameError && !string.IsNullOrWhiteSpace(_newProfileName))
        {
            ProfileNameError = string.Empty;
            OnPropertyChanged(nameof(HasProfileNameError));
        }
    }

    partial void OnNewUsernameChanged(string value)
    {
        if (value is null) _newUsername = string.Empty;
        if (HasUsernameError && !string.IsNullOrWhiteSpace(_newUsername))
        {
            UsernameError = string.Empty;
            OnPropertyChanged(nameof(HasUsernameError));
        }
    }

    partial void OnNewPasswordChanged(string value)
    {
        if (value is null) _newPassword = string.Empty;
        if (HasPasswordError && !string.IsNullOrEmpty(_newPassword))
        {
            PasswordError = string.Empty;
            OnPropertyChanged(nameof(HasPasswordError));
        }
    }

    partial void OnPresetFeedbackChanged(string value)
    {
        OnPropertyChanged(nameof(HasPresetFeedback));
    }

    private void RefreshVaultState()
    {
        NeedsVaultCreation = !_store.Exists;
        IsVaultUnlocked = _store.IsUnlocked;
        if (IsVaultUnlocked)
        {
            ReloadEntries();
        }
        else
        {
            StoredCredentials.Clear();
            OnPropertyChanged(nameof(HasStoredCredentials));
            VaultStatus = NeedsVaultCreation
                ? "Create a master password (min 12 characters) to initialize your AES-256 encrypted SSH vault"
                : "Vault locked — enter your master password to unlock saved SSH presets";
        }
        VaultChanged?.Invoke();
    }

    private void ReloadEntries()
    {
        var list = _store.LoadAllCredentials();
        StoredCredentials.Clear();
        foreach (var entry in list)
        {
            StoredCredentials.Add(entry);
        }
        OnPropertyChanged(nameof(HasStoredCredentials));
        VaultChanged?.Invoke();
    }

    [RelayCommand]
    public async Task UnlockVaultAsync()
    {
        if (IsVaultBusy) return;

        var password = VaultPassword ?? string.Empty;
        var confirmation = VaultConfirmation ?? string.Empty;
        HasVaultError = false;

        if (NeedsVaultCreation)
        {
            if (password.Length < 12)
            {
                HasVaultError = true;
                VaultStatus = "Master password must be at least 12 characters";
                return;
            }
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                HasVaultError = true;
                VaultStatus = "Master password and confirmation do not match";
                return;
            }
        }
        else if (string.IsNullOrEmpty(password))
        {
            HasVaultError = true;
            VaultStatus = "Enter your master password to unlock the vault";
            return;
        }

        IsVaultBusy = true;
        VaultStatus = NeedsVaultCreation ? "Deriving AES-256 key and creating vault..." : "Unlocking encrypted vault...";

        try
        {
            bool createNew = NeedsVaultCreation;
            await Task.Run(() =>
            {
                if (createNew) _store.Create(password);
                else _store.Unlock(password);
            });

            VaultPassword = string.Empty;
            VaultConfirmation = string.Empty;
            HasVaultError = false;
            NeedsVaultCreation = false;
            IsVaultUnlocked = true;
            ReloadEntries();
            VaultStatus = $"Vault unlocked · {StoredCredentials.Count} saved SSH preset(s)";
        }
        catch (Exception ex)
        {
            HasVaultError = true;
            VaultStatus = ex.Message;
        }
        finally
        {
            IsVaultBusy = false;
        }
    }

    [RelayCommand]
    public void LockVault()
    {
        _store.Lock();
        VaultPassword = string.Empty;
        VaultConfirmation = string.Empty;
        HasVaultError = false;
        PresetFeedback = string.Empty;
        RefreshVaultState();
    }

    public bool ValidatePresetForm()
    {
        ProfileNameError = string.IsNullOrWhiteSpace(NewProfileName)
            ? "Profile name is required (e.g., Campus Core)"
            : string.Empty;

        UsernameError = string.IsNullOrWhiteSpace(NewUsername)
            ? "SSH username is required"
            : string.Empty;

        PasswordError = string.IsNullOrEmpty(NewPassword)
            ? "SSH password is required"
            : string.Empty;

        OnPropertyChanged(nameof(HasProfileNameError));
        OnPropertyChanged(nameof(HasUsernameError));
        OnPropertyChanged(nameof(HasPasswordError));

        return !HasProfileNameError && !HasUsernameError && !HasPasswordError;
    }

    [RelayCommand]
    public async Task SavePresetAsync()
    {
        if (!IsVaultUnlocked)
        {
            PresetFeedback = "Unlock the vault before saving presets";
            return;
        }

        if (!ValidatePresetForm()) return;

        var name = NewProfileName.Trim();
        var user = NewUsername.Trim();
        var pass = NewPassword;
        var secret = NewSecret ?? string.Empty;

        IsVaultBusy = true;
        try
        {
            await Task.Run(() => _store.SaveCredential(name, user, pass, secret));
            ReloadEntries();
            NewProfileName = string.Empty;
            NewUsername = string.Empty;
            NewPassword = string.Empty;
            NewSecret = string.Empty;
            PresetFeedback = $"Saved encrypted preset '{name}'";
            VaultStatus = $"Vault unlocked · {StoredCredentials.Count} saved SSH preset(s)";
        }
        catch (Exception ex)
        {
            PresetFeedback = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsVaultBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeletePresetAsync(CredentialEntry? entry)
    {
        if (entry == null || !IsVaultUnlocked) return;

        IsVaultBusy = true;
        try
        {
            var name = entry.Name;
            await Task.Run(() => _store.DeleteCredential(name));
            ReloadEntries();
            PresetFeedback = $"Deleted preset '{name}'";
            VaultStatus = $"Vault unlocked · {StoredCredentials.Count} saved SSH preset(s)";
        }
        catch (Exception ex)
        {
            PresetFeedback = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsVaultBusy = false;
        }
    }

    [RelayCommand]
    public void ApplyPresetToSession(CredentialEntry? entry)
    {
        if (entry == null) return;
        CredentialAppliedToSession?.Invoke(entry);
        PresetFeedback = $"Applied '{entry.Name}' ({entry.Username}) to active execution session";
    }
}
