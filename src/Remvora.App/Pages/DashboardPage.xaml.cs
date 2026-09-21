using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        ViewModel.RequestNavigate += OnRequestNavigate;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void OnRequestNavigate(string destination)
    {
        var targetType = destination switch
        {
            "apps" => typeof(AppsPage),
            "monitor" => typeof(InstallMonitorPage),
            "winapps" => typeof(WindowsAppsPage),
            "startup" => typeof(StartupPage),
            "cleaner" => typeof(CleanerPage),
            "hunter" => typeof(HunterPage),
            "tools" => typeof(WindowsToolsPage),
            "audit" => typeof(AuditLogPage),
            _ => null
        };

        if (targetType is not null)
        {
            Frame.Navigate(targetType);
        }
    }
}
