using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CiscoUpgradeOrchestrator.Gui.ViewModels;
using CiscoUpgradeOrchestrator.Gui.Views;

namespace CiscoUpgradeOrchestrator.Gui;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        return data switch
        {
            DashboardViewModel => new DashboardView(),
            InventoryViewModel => new InventoryView(),
            PlannerViewModel => new PlannerView(),
            ExecutionViewModel => new ExecutionView(),
            HistoryViewModel => new HistoryView(),
            CredentialsViewModel => new CredentialsView(),
            ConfigViewModel => new ConfigView(),
            SettingsViewModel => new SettingsView(),
            _ => ResolveDynamic(data)
        };
    }

    private static Control ResolveDynamic(object data)
    {
        var fullName = data.GetType().FullName;
        if (fullName == null)
            return new TextBlock { Text = "Unable to resolve view: type name is null" };

        var viewName = fullName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(viewName) ?? data.GetType().Assembly.GetType(viewName);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "View Not Found: " + viewName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
