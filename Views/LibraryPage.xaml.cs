using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Lumen.ViewModels;
using Lumen.Services;

namespace Lumen.Views;

public sealed partial class LibraryPage : Page
{
    private readonly LibraryPageViewModel _viewModel = new();
    private bool _initializingFilters;

    public LibraryPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LibraryViewModel library)
        {
            _initializingFilters = true;
            try
            {
                LibraryDescription.Text = library.Name;
                _viewModel.SetLibrary(library);
                await _viewModel.LoadAsync();

                await Task.Yield();
                GenreComboBox.SelectedIndex = GenreComboBox.Items.Count > 0 ? 0 : -1;
                YearComboBox.SelectedIndex = YearComboBox.Items.Count > 0 ? 0 : -1;
                FilterComboBox.SelectedIndex = FilterComboBox.Items.Count > 0 ? 0 : -1;
                SortComboBox.SelectedIndex = SortComboBox.Items.Count > 0 ? 0 : -1;

                ArtworkPreloader.Preload(_viewModel.Items.Select(x => x.ImageUrl));
                UpdateCount();
            }
            finally
            {
                _initializingFilters = false;
            }
        }
    }

    private async void MusicView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string view })
        {
            _viewModel.SetMusicView(view);
            await _viewModel.LoadAsync();
            ArtworkPreloader.Preload(_viewModel.Items.Select(x => x.ImageUrl));
            UpdateCount();
        }
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _initializingFilters) return;
        _viewModel.ApplyFilters();
        UpdateCount();
    }

    private void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleSortDirection();
        UpdateCount();
    }

    private void UpdateCount() => CountText.Text = _viewModel.TotalCount > 0 ? $"• {_viewModel.TotalCount:N0} items" : string.Empty;

    private void MediaCard_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaCardViewModel item && !string.IsNullOrWhiteSpace(item.Id))
        {
            if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(MusicPlayerPage), item.Id);
            else if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(MusicAlbumPage), item.Id);
            else if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(ArtistPage), item.Id);
            else
                Frame?.Navigate(typeof(ItemPage), item.Id);
        }
    }

    // Kept for stale generated XAML.
    private void MediaCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaCardViewModel item && !string.IsNullOrWhiteSpace(item.Id))
        {
            if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(MusicPlayerPage), item.Id);
            else if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(MusicAlbumPage), item.Id);
            else if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
                Frame?.Navigate(typeof(ArtistPage), item.Id);
            else
                Frame?.Navigate(typeof(ItemPage), item.Id);
        }
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
