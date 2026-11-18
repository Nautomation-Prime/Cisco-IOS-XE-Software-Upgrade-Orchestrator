using CommunityToolkit.Mvvm.ComponentModel;

namespace CiscoUpgradeOrchestrator.Gui.Models;

public partial class CredentialEntry : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _secret = string.Empty;

    public string MaskedPassword => new string('•', System.Math.Max(6, Password?.Length ?? 6));
    public bool HasSecret => !string.IsNullOrEmpty(Secret);
}
