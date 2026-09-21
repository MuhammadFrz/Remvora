using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.Pages;

namespace Remvora.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // Navigate to Dashboard initially
        NavFrame.Navigate(typeof(DashboardPage));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "dashboard":
                    NavFrame.Navigate(typeof(DashboardPage));
                    break;
                case "apps":
                    NavFrame.Navigate(typeof(AppsPage));
                    break;
                case "winapps":
                    NavFrame.Navigate(typeof(WindowsAppsPage));
                    break;
                case "startup":
                    NavFrame.Navigate(typeof(StartupPage));
                    break;
                case "cleaner":
                    NavFrame.Navigate(typeof(CleanerPage));
                    break;
                case "hunter":
                    NavFrame.Navigate(typeof(HunterPage));
                    break;
                case "tools":
                    NavFrame.Navigate(typeof(WindowsToolsPage));
                    break;
                case "audit":
                    NavFrame.Navigate(typeof(AuditLogPage));
                    break;
                default:
                    break;
            }
        }
    }
}
