using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lumen.ViewModels;
using Lumen.Services;

namespace Lumen.Views;

public sealed partial class SearchPage : Page
{
    private readonly SearchViewModel _viewModel = new();
    private CancellationTokenSource? _debounce;

    public SearchPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string query)
            await SetQueryAsync(query);

        // Focus the page search box after navigation.
        DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));
    }

    public async Task SetQueryAsync(string query, CancellationToken ct = default)
    {
        _debounce?.Cancel();
        if (!string.Equals(SearchBox.Text, query, StringComparison.Ordinal))
            SearchBox.Text = query;

        _viewModel.Query = query;
        await _viewModel.SearchAsync(ct);

        if (query.Trim().Length >= 2)
            ArtworkPreloader.Preload(_viewModel.Results.Select(x => x.ImageUrl));
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        _debounce?.Cancel();
        _viewModel.Query = SearchBox.Text;
        await _viewModel.SearchAsync();
        ArtworkPreloader.Preload(_viewModel.Results.Select(x => x.ImageUrl));
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;

        _debounce?.Cancel();
        _viewModel.Query = SearchBox.Text;
        await _viewModel.SearchAsync();
        ArtworkPreloader.Preload(_viewModel.Results.Select(x => x.ImageUrl));
        e.Handled = true;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.Equals(_viewModel.Query, SearchBox.Text, StringComparison.Ordinal))
            return;

        _viewModel.Query = SearchBox.Text;
        _debounce?.Cancel();

        if (SearchBox.Text.Trim().Length < 2)
        {
            await _viewModel.SearchAsync();
            return;
        }

        _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _debounce.Token);
            await _viewModel.SearchAsync(_debounce.Token);
            ArtworkPreloader.Preload(_viewModel.Results.Select(x => x.ImageUrl));
        }
        catch (OperationCanceledException) { }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchFieldBorder.BorderBrush = ResourceBrush("LumenAccentSoftBrush");
        SearchFieldBorder.Background = ResourceBrush("LumenSearchFocusBrush");
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchFieldBorder.BorderBrush = ResourceBrush("LumenBorderBrush");
        SearchFieldBorder.Background = ResourceBrush("LumenSearchIdleBrush");
    }

    private void Result_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaCardViewModel item } || string.IsNullOrWhiteSpace(item.Id))
            return;

        if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
            Frame?.Navigate(typeof(MusicPlayerPage), item.Id);
        else if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
            Frame?.Navigate(typeof(MusicAlbumPage), item.Id);
        else if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
            Frame?.Navigate(typeof(ArtistPage), item.Id);
        else
            Frame?.Navigate(typeof(ItemPage), item.Id);
    }

    private void MediaCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaCardViewModel item)
        {
            MediaContextMenu.Show(element, item, Frame);
            e.Handled = true;
        }
    }

    private static void SetPosterScale(FrameworkElement card, double scale)
    {
        if (card.FindName("PosterArtwork") is Image image &&
            image.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
    }

    private static void SetCardHoverState(FrameworkElement card, bool hover)
    {
        if (card.FindName("SearchCardShell") is Border shell)
        {
            shell.Background = ResourceBrush(hover ? "LumenSurface2Brush" : "LumenSurfaceBrush");
            shell.BorderBrush = ResourceBrush(hover ? "LumenBorderStrongBrush" : "LumenBorderBrush");
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
        {
            SetPosterScale(card, 1.014);
            SetCardHoverState(card, true);
            SetQuickPlayVisibility(card, true);
        }
    }

    private void MediaCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            SetPosterScale(card, 1.0);
            SetCardHoverState(card, false);
            SetQuickPlayVisibility(card, false);
        }
    }

    private async void QuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item })
            await QuickPlayService.PlayAsync(item, Frame);
    }

    private void QuickPlayButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button && button.Parent is Border chrome)
        {
            chrome.BorderBrush = ResourceBrush("LumenAccentSoftBrush");
        }
    }

    private void QuickPlayButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button && button.Parent is Border chrome)
        {
            chrome.BorderBrush = ResourceBrush("LumenBorderStrongBrush");
        }
    }

    private void Artwork_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image) return;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, image);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private static Brush ResourceBrush(string key)
        => Application.Current.Resources[key] as Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
}
