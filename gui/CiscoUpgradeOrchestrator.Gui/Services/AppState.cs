using System;
using Avalonia;
using Avalonia.Styling;

namespace CiscoUpgradeOrchestrator.Gui.Services;

public class AppState
{
    private static AppState? _instance;
    public static AppState Instance => _instance ??= new AppState();

    // 0 = System Default, 1 = Dark Mode, 2 = Light Mode
    public int CurrentThemeIndex { get; private set; } = 1;
    public event Action<int>? ThemeChanged;

    // High-DPI UI Scaling
    public double UiScale { get; private set; } = 1.0;
    public event Action<double>? UiScaleChanged;

    public double DetectedRenderScaling { get; private set; } = 1.0;

    public void SetTheme(int themeIndex)
    {
        CurrentThemeIndex = themeIndex;
        if (Application.Current != null)
        {
            switch (themeIndex)
            {
                case 0:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                    break;
                case 1:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                case 2:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;
            }
        }
        ThemeChanged?.Invoke(themeIndex);
    }

    public void SetUiScale(double scale)
    {
        UiScale = Math.Clamp(scale, 0.75, 2.0);
        UiScaleChanged?.Invoke(UiScale);
    }

    public void SetDetectedRenderScaling(double scaling)
    {
        DetectedRenderScaling = scaling;
    }
}
