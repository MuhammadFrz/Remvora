using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class AppsPage : Page
{
    public AppsViewModel ViewModel { get; }

    public AppsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AppsViewModel>();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
