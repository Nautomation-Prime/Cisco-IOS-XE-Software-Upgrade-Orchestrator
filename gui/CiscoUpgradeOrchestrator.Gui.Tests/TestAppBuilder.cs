using Avalonia;
using Avalonia.Headless;
using CiscoUpgradeOrchestrator.Gui;

[assembly: AvaloniaTestApplication(typeof(CiscoUpgradeOrchestrator.Gui.Tests.TestAppBuilder))]

namespace CiscoUpgradeOrchestrator.Gui.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}
