using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<StartupViewModel>();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnToggleSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && ts.Tag is StartupItemViewModel item)
        {
            if (item.IsEnabled != ts.IsOn)
            {
                await ViewModel.ToggleEnabledAsync(item);
            }
        }
    }
}
