using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class WindowsToolsPage : Page
{
    public WindowsToolsViewModel ViewModel { get; }

    public WindowsToolsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WindowsToolsViewModel>();
    }

    private void CategorySelectorBar_Loaded(object sender, RoutedEventArgs e)
    {
        CategorySelectorBar.SelectedItem = AllToolsItem;
    }

    private void CategorySelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not null)
        {
            int index = sender.Items.IndexOf(sender.SelectedItem);
            ViewModel.SelectedCategoryIndex = index >= 0 ? index : 0;
        }
    }
}
