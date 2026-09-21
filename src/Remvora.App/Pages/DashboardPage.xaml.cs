using Microsoft.Extensions.DependencyInjection;
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
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
