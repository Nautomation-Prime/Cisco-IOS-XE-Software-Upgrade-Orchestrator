using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CiscoUpgradeOrchestrator.Gui;
using CiscoUpgradeOrchestrator.Gui.Models;
using CiscoUpgradeOrchestrator.Gui.Services;
using CiscoUpgradeOrchestrator.Gui.ViewModels;
using Xunit;

namespace CiscoUpgradeOrchestrator.Gui.Tests;

public class HeadlessUiTests
{
    [AvaloniaFact]
    public void MainWindowInitializesAndRenders()
    {
        var window = new MainWindow();
        window.Show();

        Assert.NotNull(window);
        Assert.Equal("Cisco IOS-XE Software Upgrade Orchestrator", window.Title);
        Assert.IsType<MainWindowViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void StepperNavigationAndUtilityViewsSwitchCorrectly()
    {
        var vm = new MainWindowViewModel();

        // Starts directly on Stage 1: Inventory
        Assert.IsType<InventoryViewModel>(vm.CurrentView);
        Assert.True(vm.IsInventoryActive);
        Assert.False(vm.IsUtilityViewActive);

        // Stage 2: Plan & Batches
        vm.SelectStage2Command.Execute(null);
        Assert.IsType<PlannerViewModel>(vm.CurrentView);
        Assert.True(vm.IsPlannerActive);

        // Stage 3: Execute
        vm.SelectStage3Command.Execute(null);
        Assert.IsType<ExecutionViewModel>(vm.CurrentView);
        Assert.True(vm.IsExecutionActive);

        // Stage 4: History
        vm.SelectStage4Command.Execute(null);
        Assert.IsType<HistoryViewModel>(vm.CurrentView);
        Assert.True(vm.IsHistoryActive);

        // Utility view: Config + ReturnToWorkflow restores last stage (History)
        vm.OpenConfigCommand.Execute(null);
        Assert.IsType<ConfigViewModel>(vm.CurrentView);
        Assert.True(vm.IsConfigActive);
        Assert.True(vm.IsUtilityViewActive);

        vm.ReturnToWorkflowCommand.Execute(null);
        Assert.IsType<HistoryViewModel>(vm.CurrentView);
        Assert.False(vm.IsUtilityViewActive);

        // Utility view: Settings
        vm.OpenSettingsCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(vm.CurrentView);
        Assert.True(vm.IsSettingsActive);
    }

    [AvaloniaFact]
    public void SetupStagesLockWhileExecutionIsRunning()
    {
        var vm = new MainWindowViewModel();
        vm.SelectStage3Command.Execute(null);
        Assert.IsType<ExecutionViewModel>(vm.CurrentView);

        // Simulate active upgrade execution
        vm.Execution.IsRunning = true;
        Assert.True(vm.IsExecutionRunning);
        Assert.False(vm.CanNavigateToSetupStages);

        // Attempt to jump back to Stage 1 or Stage 2 must be blocked
        vm.SelectStage1Command.Execute(null);
        Assert.IsType<ExecutionViewModel>(vm.CurrentView);

        vm.SelectStage2Command.Execute(null);
        Assert.IsType<ExecutionViewModel>(vm.CurrentView);

        // History remains accessible to inspect records
        vm.SelectStage4Command.Execute(null);
        Assert.IsType<HistoryViewModel>(vm.CurrentView);

        // Release lock
        vm.Execution.IsRunning = false;
        Assert.True(vm.CanNavigateToSetupStages);
        vm.SelectStage1Command.Execute(null);
        Assert.IsType<InventoryViewModel>(vm.CurrentView);
    }

    [AvaloniaFact]
    public async Task StagePipelinePropagatesInventoryPathAndJobId()
    {
        var vm = new MainWindowViewModel();
        vm.Inventory.DeviceCards.Clear();
        vm.Inventory.InventoryFilePath = @"C:\lab\switches.xlsx";
        await vm.Inventory.ContinueToPlanAsync();

        Assert.IsType<PlannerViewModel>(vm.CurrentView);
        Assert.Equal(@"C:\lab\switches.xlsx", vm.Planner.InventoryFilePath);

        vm.Planner.PlanJobId = "job-20261118-01";
        vm.Planner.ProceedToExecutionCommand.Execute(null);

        Assert.IsType<ExecutionViewModel>(vm.CurrentView);
        Assert.Equal(@"C:\lab\switches.xlsx", vm.Execution.InventoryFilePath);
        Assert.Equal("job-20261118-01", vm.Execution.JobId);
    }

    [AvaloniaFact]
    public void DeviceCardInputValidationCatchesErrorsAndDuplicates()
    {
        var vm = new InventoryViewModel();
        vm.DeviceCards.Clear();
        vm.AddSwitchCommand.Execute(null);

        Assert.Single(vm.DeviceCards);
        var card1 = vm.SelectedCard!;

        // Newly added card has missing IP, TargetVersion, ImagePath, MD5
        Assert.False(card1.IsCardValid);
        Assert.True(card1.HasIpAddressError);
        Assert.True(card1.HasTargetVersionError);
        Assert.True(card1.HasImagePathError);
        Assert.True(card1.HasImageMd5Error);

        // Invalid IP, MD5, SSH Port
        card1.Hostname = "SW-CORE-01";
        card1.IpAddress = "999.999.1.1";
        card1.TargetVersion = "17.12.1";
        card1.ImagePath = @"C:\images\cat9k.bin";
        card1.ImageMd5 = "not-valid-md5";
        card1.SshPort = "70000";

        Assert.False(card1.IsCardValid);
        Assert.True(card1.HasIpAddressError);
        Assert.True(card1.HasImageMd5Error);
        Assert.True(card1.HasSshPortError);

        // Fix all fields -> Card becomes valid
        card1.IpAddress = "192.168.10.10";
        card1.ImageMd5 = "0123456789abcdef0123456789abcdef";
        card1.SshPort = "22";

        Assert.True(card1.IsCardValid);
        Assert.True(vm.IsValid);

        // Duplicate card and set identical IP -> duplicate error detected
        vm.DuplicateSelectedSwitchCommand.Execute(null);
        Assert.Equal(2, vm.DeviceCards.Count);
        var card2 = vm.SelectedCard!;
        card2.IpAddress = "192.168.10.10";

        Assert.False(vm.IsValid);
        Assert.True(card2.HasIpAddressError);

        // Unique IP resolves duplicate error
        card2.IpAddress = "192.168.10.11";
        Assert.True(vm.IsValid);

        // Remove second card
        vm.RemoveSwitchCommand.Execute(card2);
        Assert.Single(vm.DeviceCards);
        Assert.True(vm.IsValid);
    }

    [AvaloniaFact]
    public async Task SaveInventoryBridgeWritesAndValidatesCardsWithoutExcel()
    {
        var backend = PythonBackendService.Instance;
        var tempXlsx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gui_card_test_{System.Guid.NewGuid():N}.xlsx");

        try
        {
            var card = new DeviceCardViewModel
            {
                Hostname = "SW-LAB-01",
                IpAddress = "192.168.50.10",
                Platform = "catalyst9300",
                CurrentVersion = "17.9.4a",
                TargetVersion = "17.12.1",
                ImagePath = "/images/cat9k_iosxe.17.12.01.SPA.bin",
                ImageMd5 = "0123456789abcdef0123456789abcdef",
                RedundancyGroup = "lab-pair-1",
                SshPort = "22",
                ConsoleVerified = true,
                Enabled = true
            };

            var res = await backend.SaveInventoryAsync(tempXlsx, new[] { card.ToDeviceModel() });
            Assert.True(res.Success);
            Assert.True(res.Valid);
            Assert.Equal(1, res.TotalDevices);
            Assert.True(System.IO.File.Exists(tempXlsx));
        }
        finally
        {
            if (System.IO.File.Exists(tempXlsx)) System.IO.File.Delete(tempXlsx);
        }
    }

    [AvaloniaFact]
    public void OsDetectionAdaptsMacAndWindowsChromeAndShortcuts()
    {
        var macVm = new MainWindowViewModel(isMacOsOverride: true);
        Assert.True(macVm.IsMacOS);
        Assert.False(macVm.IsWindowsOrLinux);
        Assert.Equal("⌘", macVm.PlatformShortcutPrefix);
        Assert.Equal("Inventory (⌘1)", macVm.Stage1Tooltip);
        Assert.Contains("⌘,", macVm.SettingsTooltip);

        var winVm = new MainWindowViewModel(isMacOsOverride: false);
        Assert.False(winVm.IsMacOS);
        Assert.True(winVm.IsWindowsOrLinux);
        Assert.Equal("Ctrl+", winVm.PlatformShortcutPrefix);
        Assert.Equal("Inventory (Ctrl+1)", winVm.Stage1Tooltip);
        Assert.Contains("Ctrl+,", winVm.SettingsTooltip);
    }

    [AvaloniaFact]
    public void WindowsCaptionMaximizeAndRestoreGlyphsSwapOnWindowState()
    {
        var window = new MainWindow();
        window.Show();

        var maxGlyph = window.FindControl<Avalonia.Controls.Shapes.Path>("MaximizeGlyph");
        var restoreGlyph = window.FindControl<Avalonia.Controls.Shapes.Path>("RestoreGlyph");

        Assert.NotNull(maxGlyph);
        Assert.NotNull(restoreGlyph);

        window.WindowState = Avalonia.Controls.WindowState.Normal;
        Assert.True(maxGlyph!.IsVisible);
        Assert.False(restoreGlyph!.IsVisible);

        window.WindowState = Avalonia.Controls.WindowState.Maximized;
        Assert.False(maxGlyph.IsVisible);
        Assert.True(restoreGlyph.IsVisible);
    }

    [AvaloniaFact]
    public void ExecutionCommandBuilderRespectsCliSchemaForAllModes()
    {
        var vm = new ExecutionViewModel
        {
            InventoryFilePath = @"C:\lab\gui_inventory.xlsx",
            JobId = "job-plan-123",
            AutoApprove = true,
            RetryFailed = true
        };

        // Mode 2 (Resume): MUST NOT emit --yes or --dry-run
        vm.SelectedModeIndex = 2;
        vm.DryRun = true; // Should be auto-cleared
        Assert.False(vm.DryRun);
        Assert.False(vm.CanUseDryRun);
        Assert.False(vm.CanUseAutoApprove);

        var resumeArgs = vm.BuildCommandArguments(out var err, out _);
        Assert.Null(err);
        Assert.NotNull(resumeArgs);
        Assert.StartsWith("resume --job-id \"job-plan-123\"", resumeArgs);
        Assert.Contains("--retry-failed", resumeArgs);
        Assert.DoesNotContain("--yes", resumeArgs);
        Assert.DoesNotContain("--dry-run", resumeArgs);

        // Mode 1 (Stage): MUST NOT emit --dry-run, DOES emit --yes
        vm.SelectedModeIndex = 1;
        var stageArgs = vm.BuildCommandArguments(out err, out _);
        Assert.Null(err);
        Assert.StartsWith("stage --job-id \"job-plan-123\"", stageArgs);
        Assert.Contains("--yes", stageArgs);
        Assert.DoesNotContain("--dry-run", stageArgs);

        // Mode 0 (Upgrade + DryRun with existing planned JobId): MUST use -f instead of --job-id
        vm.SelectedModeIndex = 0;
        vm.DryRun = true;
        var dryRunArgs = vm.BuildCommandArguments(out err, out var infoNote);
        Assert.Null(err);
        Assert.NotNull(infoNote);
        Assert.Contains("-f \"C:\\lab\\gui_inventory.xlsx\" --dry-run", dryRunArgs);
        Assert.DoesNotContain("--job-id", dryRunArgs);
    }

    [AvaloniaFact]
    public void HistoryFilterTilesClassifyAllEngineStatuses()
    {
        var vm = new HistoryViewModel();
        vm.Jobs.Clear();
        vm.Jobs.Add(new JobModel { JobId = "j1", Status = "planned" });
        vm.Jobs.Add(new JobModel { JobId = "j2", Status = "running" });
        vm.Jobs.Add(new JobModel { JobId = "j3", Status = "staged" });
        vm.Jobs.Add(new JobModel { JobId = "j4", Status = "awaiting_cleanup" });
        vm.Jobs.Add(new JobModel { JobId = "j5", Status = "completed" });
        vm.Jobs.Add(new JobModel { JobId = "j6", Status = "halted" });
        vm.Jobs.Add(new JobModel { JobId = "j7", Status = "cancelled" });
        vm.Jobs.Add(new JobModel { JobId = "j8", Status = "failed" });

        vm.FilterJobs("Planned");
        Assert.Equal(2, vm.FilteredJobs.Count);

        vm.FilterJobs("Completed");
        Assert.Equal(3, vm.FilteredJobs.Count);

        vm.FilterJobs("Failed");
        Assert.Equal(3, vm.FilteredJobs.Count);
    }

    [AvaloniaFact]
    public void CardEditingUpdatesInPlaceWithoutReplacingCollection()
    {
        var vm = new InventoryViewModel();
        vm.DeviceCards.Clear();
        vm.FilterQuery = string.Empty;
        vm.AddSwitchCommand.Execute(null);

        var originalCollectionRef = vm.FilteredCards;
        var card = vm.SelectedCard!;

        // Editing fields should NOT recreate FilteredCards (prevents ListBox flicker/lag)
        card.Hostname = "SW-FAST-01";
        card.IpAddress = "10.0.0.10";
        card.TargetVersion = "17.12.1";
        card.ImagePath = "/images/cat9k.bin";
        card.ImageMd5 = "0123456789abcdef0123456789abcdef";

        Assert.Same(originalCollectionRef, vm.FilteredCards);
        Assert.True(card.IsCardValid);
        Assert.True(vm.IsValid);
    }

    [AvaloniaFact]
    public void DeviceCardHandlesNullFromComboBoxDetachWithoutCrashing()
    {
        var vm = new InventoryViewModel();
        vm.DeviceCards.Clear();
        vm.AddSwitchCommand.Execute(null);

        var card = vm.SelectedCard!;
        card.Platform = "catalyst9200";

        // Simulate Avalonia ComboBox / TextBox writing null during DataContext switch
        card.Platform = null!;
        card.Hostname = null!;
        card.IpAddress = null!;
        card.TargetVersion = null!;
        card.ImagePath = null!;
        card.ImageMd5 = null!;

        // Previous valid platform is preserved; ToDeviceModel and AddSwitch must never throw NRE
        Assert.Equal("catalyst9200", card.Platform);
        var model = card.ToDeviceModel();
        Assert.NotNull(model);
        Assert.Equal("catalyst9200", model.Platform);

        vm.AddSwitchCommand.Execute(null);
        Assert.Equal(2, vm.DeviceCards.Count);
    }

    [AvaloniaFact]
    public async Task PlannerDependsOnStage1AndAutoSyncsDirtyCards()
    {
        var vm = new MainWindowViewModel();
        var tempXlsx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"stage1_sync_{System.Guid.NewGuid():N}.xlsx");

        try
        {
            vm.Inventory.DeviceCards.Clear();
            vm.Inventory.InventoryFilePath = tempXlsx;
            vm.Inventory.AddSwitchCommand.Execute(null);

            var card = vm.Inventory.SelectedCard!;
            card.Hostname = "SW-STAGE1-SYNC";
            card.IpAddress = "192.168.77.10";
            card.Platform = "catalyst9300";
            card.TargetVersion = "17.12.1";
            card.ImagePath = "/images/cat9k_iosxe.17.12.01.SPA.bin";
            card.ImageMd5 = "0123456789abcdef0123456789abcdef";

            Assert.True(vm.Inventory.IsDirty);
            Assert.True(vm.Planner.IsStage1Ready);

            // Jump straight to Stage 2 and click Generate Plan WITHOUT manually clicking Save in Stage 1
            vm.SelectStage2Command.Execute(null);
            await vm.Planner.GeneratePlanAsync();

            Assert.True(System.IO.File.Exists(tempXlsx));
            Assert.False(vm.Inventory.IsDirty);
            Assert.True(vm.Planner.HasPlan);
            Assert.Single(vm.Planner.Batches);
            Assert.Contains("SW-STAGE1-SYNC", vm.Planner.Batches[0].Devices);
        }
        finally
        {
            if (System.IO.File.Exists(tempXlsx)) System.IO.File.Delete(tempXlsx);

            // Clean up test job row from SQLite so running tests never pollutes History
            if (!string.IsNullOrWhiteSpace(vm.Planner.PlanJobId))
            {
                var dbPath = System.IO.Path.Combine(PythonBackendService.Instance.ProjectRoot, "state", "orchestrator.db");
                if (System.IO.File.Exists(dbPath))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = PythonBackendService.Instance.PythonPath,
                            Arguments = $"-c \"import sqlite3; c=sqlite3.connect(r'{dbPath}'); c.execute('PRAGMA foreign_keys=ON'); c.execute('DELETE FROM jobs WHERE job_id=?', ('{vm.Planner.PlanJobId}',)); c.commit(); c.close()\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(5000);
                    }
                    catch { }
                }
            }
        }
    }

    [AvaloniaFact]
    public void HistoryFilterTilesFilterJobsByStatus()
    {
        var vm = new HistoryViewModel();
        vm.Jobs.Clear();
        vm.Jobs.Add(new JobModel { JobId = "job-1", Status = "planned" });
        vm.Jobs.Add(new JobModel { JobId = "job-2", Status = "completed" });
        vm.Jobs.Add(new JobModel { JobId = "job-3", Status = "failed" });

        vm.FilterJobs("All");
        Assert.Equal(3, vm.FilteredJobs.Count);
        Assert.True(vm.IsFilterAll);

        vm.FilterJobs("Planned");
        Assert.Single(vm.FilteredJobs);
        Assert.Equal("job-1", vm.FilteredJobs[0].JobId);

        vm.FilterJobs("Completed");
        Assert.Single(vm.FilteredJobs);
        Assert.Equal("job-2", vm.FilteredJobs[0].JobId);

        vm.FilterJobs("Failed");
        Assert.Single(vm.FilteredJobs);
        Assert.Equal("job-3", vm.FilteredJobs[0].JobId);
    }

    [AvaloniaFact]
    public void ThemeSwitchingSynchronizesState()
    {
        var appState = AppState.Instance;
        var vm = new MainWindowViewModel();

        // 1 = Dark Mode
        appState.SetTheme(1);
        Assert.Equal(1, appState.CurrentThemeIndex);
        Assert.True(vm.IsDark);

        // 2 = Light Mode
        appState.SetTheme(2);
        Assert.Equal(2, appState.CurrentThemeIndex);
        Assert.False(vm.IsDark);

        // 0 = System Default
        appState.SetTheme(0);
        Assert.Equal(0, appState.CurrentThemeIndex);
    }

    [AvaloniaFact]
    public void UiScaleClampsWithinBounds()
    {
        var appState = AppState.Instance;

        appState.SetUiScale(1.25);
        Assert.Equal(1.25, appState.UiScale);

        // Clamps at 0.75 minimum
        appState.SetUiScale(0.1);
        Assert.Equal(0.75, appState.UiScale);

        // Clamps at 2.0 maximum
        appState.SetUiScale(5.0);
        Assert.Equal(2.0, appState.UiScale);

        // Reset
        appState.SetUiScale(1.0);
        Assert.Equal(1.0, appState.UiScale);
    }

    [AvaloniaFact]
    public void InventoryFilteringFiltersDevices()
    {
        var vm = new InventoryViewModel();
        vm.AllDevices.Clear();

        vm.AllDevices.Add(new DeviceModel
        {
            Hostname = "SW-ACCESS-01",
            IpAddress = "192.168.1.10",
            Platform = "catalyst9300",
            TargetVersion = "17.12.1"
        });

        vm.AllDevices.Add(new DeviceModel
        {
            Hostname = "SW-DIST-02",
            IpAddress = "192.168.2.20",
            Platform = "catalyst9200",
            TargetVersion = "17.9.4"
        });

        // Query matches SW-ACCESS
        vm.FilterQuery = "access";
        Assert.Single(vm.FilteredDevices);
        Assert.Equal("SW-ACCESS-01", vm.FilteredDevices[0].Hostname);

        // Query matches IP
        vm.FilterQuery = "192.168.2";
        Assert.Single(vm.FilteredDevices);
        Assert.Equal("SW-DIST-02", vm.FilteredDevices[0].Hostname);

        // Empty query restores all
        vm.FilterQuery = string.Empty;
        Assert.Equal(2, vm.FilteredDevices.Count);
    }

    [AvaloniaFact]
    public void PythonBackendServiceResolvesProjectRootAndVenv()
    {
        var backend = PythonBackendService.Instance;
        Assert.NotNull(backend.ProjectRoot);
        Assert.NotEmpty(backend.ProjectRoot);
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(backend.ProjectRoot, "orchestrator.py")));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(backend.ProjectRoot, "gui_bridge.py")));
        Assert.Contains("python", backend.PythonPath);
    }

    [AvaloniaFact]
    public void ExecutionViewModelDefaultValuesAndArgumentState()
    {
        var vm = new ExecutionViewModel();
        Assert.False(vm.IsRunning);
        Assert.False(vm.DryRun);
        Assert.True(vm.AutoApprove);
        Assert.Equal(0, vm.SelectedModeIndex); // Upgrade

        vm.DryRun = true;
        Assert.True(vm.DryRun);

        // Switching to Stage mode clears DryRun (orchestrator.py stage does not accept --dry-run)
        vm.SelectedModeIndex = 1; // Stage
        Assert.Equal(1, vm.SelectedModeIndex);
        Assert.False(vm.DryRun);

        vm.ClearConsole();
        Assert.Equal(string.Empty, vm.ConsoleOutput);
    }

    [AvaloniaFact]
    public void PlannerViewModelInitialState()
    {
        var vm = new PlannerViewModel();
        Assert.False(vm.HasPlan);
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.Batches);
        Assert.Empty(vm.Commands);
    }

    [AvaloniaFact]
    public void ViewLocatorResolvesAllViewModelsToViews()
    {
        var locator = new ViewLocator();

        Assert.IsType<Views.DashboardView>(locator.Build(new DashboardViewModel()));
        Assert.IsType<Views.InventoryView>(locator.Build(new InventoryViewModel()));
        Assert.IsType<Views.PlannerView>(locator.Build(new PlannerViewModel()));
        Assert.IsType<Views.ExecutionView>(locator.Build(new ExecutionViewModel()));
        Assert.IsType<Views.HistoryView>(locator.Build(new HistoryViewModel()));
        Assert.IsType<Views.CredentialsView>(locator.Build(new CredentialsViewModel()));
        Assert.IsType<Views.ConfigView>(locator.Build(new ConfigViewModel()));
        Assert.IsType<Views.SettingsView>(locator.Build(new SettingsViewModel()));
    }

    [AvaloniaFact]
    public async Task EncryptedCredentialVaultRoundTripAndValidation()
    {
        var tempVaultDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cisco_vault_test_{System.Guid.NewGuid():N}");
        try
        {
            using var store = new CredentialStore(tempVaultDir);
            var vm = new CredentialsViewModel(store);

            Assert.True(vm.NeedsVaultCreation);
            Assert.False(vm.IsVaultUnlocked);

            // Short master password rejected
            vm.VaultPassword = "short";
            vm.VaultConfirmation = "short";
            await vm.UnlockVaultAsync();
            Assert.False(vm.IsVaultUnlocked);
            Assert.True(vm.HasVaultError);

            // Valid 12+ char password creates & unlocks vault
            vm.VaultPassword = "SuperStrongVaultPassword!2026";
            vm.VaultConfirmation = "SuperStrongVaultPassword!2026";
            await vm.UnlockVaultAsync();
            Assert.True(vm.IsVaultUnlocked);
            Assert.False(vm.NeedsVaultCreation);

            // Empty preset form triggers inline field validation
            Assert.False(vm.ValidatePresetForm());
            Assert.True(vm.HasProfileNameError);
            Assert.True(vm.HasUsernameError);
            Assert.True(vm.HasPasswordError);

            // Save preset
            vm.NewProfileName = "Campus Core";
            vm.NewUsername = "netadmin";
            vm.NewPassword = "SwitchPassword!99";
            vm.NewSecret = "EnableSecret!99";
            await vm.SavePresetAsync();

            Assert.Single(vm.StoredCredentials);
            Assert.Equal("Campus Core", vm.StoredCredentials[0].Name);
            Assert.True(vm.StoredCredentials[0].HasSecret);

            // Lock clears in-memory entries
            vm.LockVault();
            Assert.False(vm.IsVaultUnlocked);
            Assert.Empty(vm.StoredCredentials);

            // Wrong password fails cleanly
            vm.VaultPassword = "WrongPassword123456";
            await vm.UnlockVaultAsync();
            Assert.False(vm.IsVaultUnlocked);
            Assert.True(vm.HasVaultError);

            // Correct password restores saved preset
            vm.VaultPassword = "SuperStrongVaultPassword!2026";
            await vm.UnlockVaultAsync();
            Assert.True(vm.IsVaultUnlocked);
            Assert.Single(vm.StoredCredentials);
            Assert.Equal("netadmin", vm.StoredCredentials[0].Username);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempVaultDir))
                System.IO.Directory.Delete(tempVaultDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ExecutionRequiresValidatedGuiSshCredentials()
    {
        var vm = new ExecutionViewModel();

        // Missing username & password fails validation
        Assert.False(vm.ValidateExecutionInputs());
        Assert.True(vm.HasUsernameError);
        Assert.True(vm.HasPasswordError);

        // Applying a vault preset satisfies validation immediately
        vm.ApplyCredentialPreset(new CredentialEntry
        {
            Name = "Lab Switches",
            Username = "admin",
            Password = "LabPassword123!",
            Secret = "Enable123!"
        });

        Assert.True(vm.ValidateExecutionInputs());
        Assert.False(vm.HasUsernameError);
        Assert.False(vm.HasPasswordError);
        Assert.Equal("admin", vm.SessionUsername);
        Assert.Equal("Preset: Lab Switches", vm.ActiveCredentialLabel);
    }

    [AvaloniaFact]
    public async Task HistoryDeleteJobAndClearHistorySafelyRemoveInactiveJobs()
    {
        var backend = PythonBackendService.Instance;
        var tempXlsx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hist_del_{System.Guid.NewGuid():N}.xlsx");
        try
        {
            var card = new DeviceCardViewModel
            {
                Hostname = "SW-DEL-01",
                IpAddress = "192.168.88.10",
                Platform = "catalyst9300",
                TargetVersion = "17.12.1",
                ImagePath = "/images/cat9k.bin",
                ImageMd5 = "0123456789abcdef0123456789abcdef"
            };
            var saveRes = await backend.SaveInventoryAsync(tempXlsx, new[] { card.ToDeviceModel() });
            Assert.True(saveRes.Success);

            var plan1 = await backend.CreatePlanAsync(tempXlsx);
            var plan2 = await backend.CreatePlanAsync(tempXlsx);
            Assert.True(plan1.Success && plan1.Plan != null);
            Assert.True(plan2.Success && plan2.Plan != null);

            // Delete single job
            var del1 = await backend.DeleteJobAsync(plan1.Plan!.JobId);
            Assert.True(del1.Success, $"DeleteJobAsync failed: error='{del1.Error}', lastErr='{backend.LastError}', lastCmd='{backend.LastCommandExecuted}'");
            Assert.Equal(1, del1.Deleted);

            // Clear remaining inactive history
            var cleared = await backend.ClearHistoryAsync();
            Assert.True(cleared.Success);
            Assert.True(cleared.Deleted >= 1);

            var remaining = await backend.GetJobsAsync();
            Assert.Empty(remaining);
        }
        finally
        {
            if (System.IO.File.Exists(tempXlsx)) System.IO.File.Delete(tempXlsx);
        }
    }

    [AvaloniaFact]
    public void SettingsViewModelSynchronizesThemeAndScale()
    {
        var vm = new SettingsViewModel();
        Assert.NotNull(vm.PythonExecutable);
        Assert.NotEmpty(vm.ProjectRoot);

        vm.ThemeIndex = 1; // Dark
        Assert.Equal(1, AppState.Instance.CurrentThemeIndex);

        vm.UiScale = 1.25;
        Assert.Equal(1.25, AppState.Instance.UiScale);

        vm.ResetUiScale();
        Assert.Equal(1.0, vm.UiScale);
        Assert.Equal(1.0, AppState.Instance.UiScale);
    }

    [AvaloniaFact]
    public void PythonBackendFindsAvailableInterpretersAndBridge()
    {
        var backend = PythonBackendService.Instance;
        var interpreters = backend.FindAvailablePythonInterpreters();
        Assert.NotNull(interpreters);
        Assert.NotEmpty(interpreters);

        var bridgeFile = System.IO.Path.Combine(backend.ProjectRoot, "gui_bridge.py");
        Assert.True(System.IO.File.Exists(bridgeFile));
    }
}
