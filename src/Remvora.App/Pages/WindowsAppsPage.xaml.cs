using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class WindowsAppsPage : Page
{
    public WindowsAppsViewModel ViewModel { get; }

    public WindowsAppsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WindowsAppsViewModel>();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
