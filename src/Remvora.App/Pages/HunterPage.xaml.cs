using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Remvora.App.ViewModels;
using Remvora.Core.Domain.Applications;

namespace Remvora.App.Pages;

public sealed partial class HunterPage : Page
{
    public HunterViewModel ViewModel { get; }

    public HunterPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HunterViewModel>();
        ViewModel.RequestUninstall += OnRequestUninstall;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadActiveWindowsAsync();
    }

    private void CrosshairButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CrosshairButton.CapturePointer(e.Pointer);
        ViewModel.IsCapturingCrosshair = true;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    }

    private void CrosshairButton_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Visual cursor feedback is active
    }

    private async void CrosshairButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        CrosshairButton.ReleasePointerCapture(e.Pointer);
        ViewModel.IsCapturingCrosshair = false;
        ProtectedCursor = null;

        if (GetCursorPos(out var pt))
        {
            await ViewModel.AcquireTargetFromPointAsync((pt.X, pt.Y));
        }
    }

    private void CrosshairButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.IsCapturingCrosshair = false;
        ProtectedCursor = null;
    }

    private void TargetSurface_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Target application or shortcut with Hunter Mode";
        }
    }

    private async void TargetSurface_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count > 0 && !string.IsNullOrWhiteSpace(items[0].Path))
            {
                await ViewModel.AcquireTargetFromPathAsync(items[0].Path);
            }
        }
    }

    private async void OnRequestUninstall(ApplicationRecord app, bool forced)
    {
        var appsVm = App.Services.GetRequiredService<AppsViewModel>();
        var itemVm = new ApplicationItemViewModel(app);

        // Navigate to AppsPage
        Frame.Navigate(typeof(AppsPage));

        if (forced)
        {
            await appsVm.StartForcedUninstallAsync(itemVm);
        }
        else
        {
            await appsVm.StartCompleteUninstallAsync(itemVm);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
