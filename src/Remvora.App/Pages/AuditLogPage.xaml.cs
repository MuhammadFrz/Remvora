using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Remvora.App.ViewModels;

namespace Remvora.App.Pages;

public sealed partial class AuditLogPage : Page
{
    public AuditLogViewModel ViewModel { get; }

    public AuditLogPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AuditLogViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadEventsAsync();
    }

    private void CategorySelectorBar_Loaded(object sender, RoutedEventArgs e)
    {
        CategorySelectorBar.SelectedItem = AllEventsItem;
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
