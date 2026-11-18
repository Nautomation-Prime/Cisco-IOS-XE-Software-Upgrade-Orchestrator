using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CiscoUpgradeOrchestrator.Gui.ViewModels;

namespace CiscoUpgradeOrchestrator.Gui.Views;

public partial class ExecutionView : UserControl
{
    private ExecutionViewModel? _vm;

    public ExecutionView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as ExecutionViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExecutionViewModel.ConsoleOutput) && ConsoleScrollViewer != null)
        {
            ConsoleScrollViewer.ScrollToEnd();
        }
    }

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null && DataContext is ExecutionViewModel vm && !string.IsNullOrEmpty(vm.ConsoleOutput))
        {
            await topLevel.Clipboard.SetTextAsync(vm.ConsoleOutput);
        }
    }
}
