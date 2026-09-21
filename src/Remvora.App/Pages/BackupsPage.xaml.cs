using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class BackupsPage : Page
{
    public BackupsViewModel ViewModel { get; }

    public BackupsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<BackupsViewModel>();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
