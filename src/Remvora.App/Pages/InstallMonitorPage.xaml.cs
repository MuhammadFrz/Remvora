using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class InstallMonitorPage : Page
{
    public InstallMonitorViewModel ViewModel { get; }

    public InstallMonitorPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<InstallMonitorViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadSessionsAsync();
    }

    private async void BrowseInstaller_Click(object sender, RoutedEventArgs e)
    {
        var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".msi");
        picker.FileTypeFilter.Add(".bat");
        picker.FileTypeFilter.Add(".cmd");

        if (App.MainWindowInstance is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.SelectedInstallerPath = file.Path;
            if (string.IsNullOrWhiteSpace(ViewModel.NewSessionName))
            {
                ViewModel.NewSessionName = System.IO.Path.GetFileNameWithoutExtension(file.Path);
            }
        }
    }
}
