using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CiscoUpgradeOrchestrator.Gui.Services;
using CiscoUpgradeOrchestrator.Gui.ViewModels;

namespace CiscoUpgradeOrchestrator.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        // Auto-detect high-DPI scaling
        ScalingChanged += (s, e) =>
        {
            AppState.Instance.SetDetectedRenderScaling(RenderScaling);
        };
        Loaded += (s, e) =>
        {
            AppState.Instance.SetDetectedRenderScaling(RenderScaling);
            UpdateMaximizeIcon();
        };

        // Observe WindowState changes to swap normal Maximize (□) and Restore (❐) glyphs
        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
            }
        };

        // OS-Aware Keyboard Navigation (⌘ Meta on macOS, Ctrl on Windows/Linux)
        KeyDown += (s, e) =>
        {
            if (DataContext is not MainWindowViewModel viewModel) return;

            var primaryMod = viewModel.IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control;
            if (!e.KeyModifiers.HasFlag(primaryMod)) return;

            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    viewModel.SelectStage1Command.Execute(null);
                    e.Handled = true;
                    break;
                case Key.D2:
                case Key.NumPad2:
                    viewModel.SelectStage2Command.Execute(null);
                    e.Handled = true;
                    break;
                case Key.D3:
                case Key.NumPad3:
                    viewModel.SelectStage3Command.Execute(null);
                    e.Handled = true;
                    break;
                case Key.D4:
                case Key.NumPad4:
                    viewModel.SelectStage4Command.Execute(null);
                    e.Handled = true;
                    break;
                case Key.OemComma:
                    viewModel.OpenSettingsCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.K:
                    viewModel.OpenCredentialsCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.E:
                    viewModel.OpenConfigCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.N:
                    viewModel.AddSwitchFromShortcutCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    viewModel.ZoomInCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    viewModel.ZoomOutCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    viewModel.ResetZoomCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.W when viewModel.IsMacOS:
                    Close();
                    e.Handled = true;
                    break;
            }
        };
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeIcon()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        if (MaximizeGlyph != null)
        {
            MaximizeGlyph.IsVisible = !isMaximized;
        }
        if (RestoreGlyph != null)
        {
            RestoreGlyph.IsVisible = isMaximized;
        }
    }
}
