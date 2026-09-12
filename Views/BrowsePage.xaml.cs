using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Lumen.Services;
using Lumen.ViewModels;

namespace Lumen.Views;

public sealed partial class BrowsePage : Page
{
    private readonly BrowsePageViewModel _viewModel = new();
    public BrowsePage() { InitializeComponent(); DataContext = _viewModel; }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is BrowseNavigationParameter parameter)
        {
            await _viewModel.LoadAsync(parameter);
            ArtworkPreloader.Preload(_viewModel.Items.Select(x => x.ImageUrl));
        }
    }

    private void MediaCard_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaCardViewModel item && !string.IsNullOrWhiteSpace(item.Id))
            Frame.Navigate(typeof(ItemPage), item.Id);
    }

    private void MediaCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaCardViewModel item)
        {
            MediaContextMenu.Show(element, item, Frame);
            e.Handled = true;
        }
    }

    private static void SetQuickPlayVisibility(FrameworkElement card, bool visible)
    {
        if (card.FindName("QuickPlayChrome") is Grid chrome)
        {
            chrome.Opacity = visible ? 1 : 0;
            chrome.IsHitTestVisible = visible;
        }
    }

    private void MediaCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
            SetQuickPlayVisibility(card, true);
    }

    private void MediaCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
            SetQuickPlayVisibility(card, false);
    }

    private async void QuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item })
            await QuickPlayService.PlayAsync(item, Frame);
    }

}
