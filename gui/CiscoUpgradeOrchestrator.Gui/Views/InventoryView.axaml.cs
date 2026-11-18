using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CiscoUpgradeOrchestrator.Gui.ViewModels;

namespace CiscoUpgradeOrchestrator.Gui.Views;

public partial class InventoryView : UserControl
{
    private static readonly Regex IosXeVersionRegex = new(@"(\d+\.\d+\.\d+[a-z]?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public InventoryView()
    {
        InitializeComponent();
    }

    private async void OnBrowseInventoryClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Excel Inventory Workbook",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel Workbooks (*.xlsx)")
                {
                    Patterns = new[] { "*.xlsx" }
                },
                new FilePickerFileType("All Files (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0 && DataContext is InventoryViewModel vm)
        {
            vm.InventoryFilePath = files[0].Path.LocalPath;
            await vm.LoadInventoryAsync();
        }
    }

    private async void OnBrowseImageClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Cisco IOS-XE Image (.bin)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Cisco IOS-XE Images (*.bin)")
                {
                    Patterns = new[] { "*.bin" }
                },
                new FilePickerFileType("All Files (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0 && DataContext is InventoryViewModel vm && vm.SelectedCard != null)
        {
            var localPath = files[0].Path.LocalPath;
            vm.SelectedCard.ImagePath = localPath;

            // Auto-infer target version from filename (e.g., cat9k_iosxe.17.12.01.SPA.bin -> 17.12.01) if blank
            if (string.IsNullOrWhiteSpace(vm.SelectedCard.TargetVersion))
            {
                var match = IosXeVersionRegex.Match(Path.GetFileName(localPath));
                if (match.Success)
                {
                    vm.SelectedCard.TargetVersion = match.Groups[1].Value;
                }
            }
        }
    }
}
