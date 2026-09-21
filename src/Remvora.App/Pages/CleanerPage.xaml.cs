using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class CleanerPage : Page
{
    public CleanerViewModel ViewModel { get; }

    public CleanerPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<CleanerViewModel>();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
