using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lumen.ViewModels;
using Lumen.Services;
using Windows.Media.Core;

namespace Lumen.Views;

public sealed partial class HomePage : Page
{
    private static readonly HomeViewModel _viewModel = new();
    private readonly DispatcherQueueTimer _featuredTimer;
    private readonly DispatcherQueueTimer _heroTrailerTimer;
    private int _heroTrailerGeneration;

    public static void NotifyPlaybackPositionChanged(string itemId, long positionTicks, long runtimeTicks)
    {
        _viewModel.UpdateContinueWatchingProgress(itemId, positionTicks, runtimeTicks);
        HomeViewModel.InvalidateCache();
    }

    public HomePage()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _featuredTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _featuredTimer.Interval = TimeSpan.FromSeconds(7);
        _featuredTimer.Tick += FeaturedTimer_Tick;

        _heroTrailerTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _heroTrailerTimer.Interval = TimeSpan.FromSeconds(3);
        _heroTrailerTimer.IsRepeating = false;
        _heroTrailerTimer.Tick += HeroTrailerTimer_Tick;
        PointerEntered += HomePage_PointerEntered;
        PointerExited += HomePage_PointerExited;

        // Keep media rows wheel-locked; wheel input scrolls the page vertically.
        AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(HomePage_PointerWheelChanged), true);

        Loaded += HomePage_Loaded;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        var itemControls = new ItemsControl[]
        {
            ContinueItemsControl,
            UpNextItemsControl,
            FavoritesItemsControl,
            RecentlyReleasedMoviesItemsControl,
            RecentlyReleasedShowsItemsControl,
            RecentlyAddedMoviesItemsControl,
            RecentlyAddedShowsItemsControl
        };

        foreach (var items in itemControls)
            items.AddHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(HorizontalItemsControl_PointerWheelChanged), true);
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_viewModel.HasFreshCache)
            await _viewModel.LoadAsync();
        ArtworkPreloader.Preload(
            _viewModel.FeaturedItems.Select(x => x.BackdropUrl)
                .Concat(_viewModel.ContinueWatching.Select(x => x.ImageUrl))
                .Concat(_viewModel.UpNext.Select(x => x.ImageUrl))
                .Concat(_viewModel.Favorites.Select(x => x.ImageUrl)));
        UpdatePersonalizedRowsLayout();
        ApplyHomeLayout();
        UpdateRowWidths();
        if (_viewModel.FeaturedItems.Count > 1)
            _featuredTimer.Start();
        ScheduleHeroTrailer();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _featuredTimer.Stop();
        _heroTrailerTimer.Stop();
        StopHeroTrailer();
        base.OnNavigatedFrom(e);
    }

    private async void FeaturedTimer_Tick(DispatcherQueueTimer sender, object args)
        => await AnimateFeaturedChangeAsync(_viewModel.ShowNextFeatured);

    private void HomePage_PointerEntered(object sender, PointerRoutedEventArgs e) => _featuredTimer.Stop();

    private void HomePage_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.FeaturedItems.Count > 1)
            _featuredTimer.Start();
    }

    private async void PreviousFeatured_Click(object sender, RoutedEventArgs e)
    {
        await AnimateFeaturedChangeAsync(_viewModel.ShowPreviousFeatured);
        RestartFeaturedTimer();
    }

    private async void NextFeatured_Click(object sender, RoutedEventArgs e)
    {
        await AnimateFeaturedChangeAsync(_viewModel.ShowNextFeatured);
        RestartFeaturedTimer();
    }

    private async Task AnimateFeaturedChangeAsync(Action change)
    {
        if (FeaturedHero.Visibility != Visibility.Visible)
        {
            change();
            return;
        }

        await AnimateOpacityAsync(FeaturedHero, FeaturedHero.Opacity, 0.18, 120);
        StopHeroTrailer();
        change();
        await Task.Yield();
        await AnimateOpacityAsync(FeaturedHero, 0.18, 1.0, 260);
        ScheduleHeroTrailer();
    }

    private static Task AnimateOpacityAsync(UIElement element, double from, double to, int milliseconds)
    {
        var completion = new TaskCompletionSource<bool>();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EnableDependentAnimation = true
        };
        animation.Completed += (_, _) => completion.TrySetResult(true);
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
        return completion.Task;
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

    private void ScheduleHeroTrailer()
    {
        _heroTrailerTimer.Stop();
        StopHeroTrailer();
        if (!App.Settings.HeroTrailersEnabled || _viewModel.FeaturedItem is null) return;
        _heroTrailerTimer.Start();
    }

    private async void HeroTrailerTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!App.Settings.HeroTrailersEnabled || _viewModel.FeaturedItem is null) return;
        var generation = ++_heroTrailerGeneration;
        try
        {
            var url = await App.Jellyfin.GetThemeVideoUrlAsync(_viewModel.FeaturedItem.Id);
            if (generation != _heroTrailerGeneration || string.IsNullOrWhiteSpace(url)) return;
            HeroTrailer.Source = MediaSource.CreateFromUri(new Uri(url));
            HeroTrailer.MediaPlayer.IsMuted = true;
            HeroTrailer.MediaPlayer.IsLoopingEnabled = true;
            HeroTrailer.Visibility = Visibility.Visible;
            HeroTrailer.MediaPlayer.Play();
        }
        catch { StopHeroTrailer(); }
    }

    private void StopHeroTrailer()
    {
        ++_heroTrailerGeneration;
        try { HeroTrailer.MediaPlayer?.Pause(); } catch { }
        HeroTrailer.Source = null;
        HeroTrailer.Visibility = Visibility.Collapsed;
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
            SetQuickPlayVisibility(card, true);
        }
    }

    private void MediaCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            SetPosterScale(card, 1.0);
            SetQuickPlayVisibility(card, false);
        }
    }

    private async void QuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item })
            await QuickPlayService.PlayAsync(item, Frame);
    }

    private void RestartFeaturedTimer()
    {
        _featuredTimer.Stop();
        if (_viewModel.FeaturedItems.Count > 1)
            _featuredTimer.Start();
    }

    // Kept for stale generated XAML.
    private void HomeScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.Handled)
            MoveHomeVertically(e);
    }

    private void HorizontalItemsControl_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        MoveHomeVertically(e);
    }

    private void HorizontalRow_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        MoveHomeVertically(e);
    }

    private void HomePage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled) return;
        MoveHomeVertically(e);
    }

    private void MoveHomeVertically(PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(HomeScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var nextOffset = Math.Clamp(
            HomeScrollViewer.VerticalOffset - delta,
            0,
            Math.Max(0, HomeScrollViewer.ScrollableHeight));

        HomeScrollViewer.ChangeView(null, nextOffset, null, true);
        e.Handled = true;
    }

    private void ContinuePrevious_Click(object sender, RoutedEventArgs e) => ScrollRow(ContinueScrollViewer, -1);
    private void ContinueNext_Click(object sender, RoutedEventArgs e) => ScrollRow(ContinueScrollViewer, 1);
    private void UpNextPrevious_Click(object sender, RoutedEventArgs e) => ScrollRow(UpNextScrollViewer, -1);
    private void UpNextNext_Click(object sender, RoutedEventArgs e) => ScrollRow(UpNextScrollViewer, 1);
    private void FavoritesPrevious_Click(object sender, RoutedEventArgs e) => ScrollRow(FavoritesScrollViewer, -1);
    private void FavoritesNext_Click(object sender, RoutedEventArgs e) => ScrollRow(FavoritesScrollViewer, 1);

    private void ApplyHomeLayout()
    {
        UIElement[] sections = App.Settings.HomeLayoutIndex switch
        {
            1 => [FavoritesPanel, PersonalizedRowsGrid, ReleasedMoviesPanel, ReleasedShowsPanel, AddedMoviesPanel, AddedShowsPanel],
            2 => [ReleasedMoviesPanel, ReleasedShowsPanel, AddedMoviesPanel, AddedShowsPanel, PersonalizedRowsGrid, FavoritesPanel],
            _ => [PersonalizedRowsGrid, FavoritesPanel, ReleasedMoviesPanel, ReleasedShowsPanel, AddedMoviesPanel, AddedShowsPanel]
        };

        foreach (var section in sections)
            HomeSectionsPanel.Children.Remove(section);
        foreach (var section in sections)
            HomeSectionsPanel.Children.Add(section);
    }

    private void UpdatePersonalizedRowsLayout()
    {
        // Reset retained grid state before recomputing personalised rows.
        Grid.SetColumn(ContinueWatchingPanel, 0);
        Grid.SetColumnSpan(ContinueWatchingPanel, 1);
        Grid.SetColumn(UpNextPanel, 2);
        Grid.SetColumnSpan(UpNextPanel, 1);

        if (_viewModel.HasContinueWatching && _viewModel.HasUpNext)
            return;

        if (_viewModel.HasContinueWatching)
        {
            Grid.SetColumn(ContinueWatchingPanel, 0);
            Grid.SetColumnSpan(ContinueWatchingPanel, 3);
            ContinueScrollViewer.ChangeView(0, null, null, true);
        }
        else if (_viewModel.HasUpNext)
        {
            Grid.SetColumn(UpNextPanel, 0);
            Grid.SetColumnSpan(UpNextPanel, 3);
            UpNextScrollViewer.ChangeView(0, null, null, true);
        }
    }

    private void UpdateRowWidths()
    {
        SetRowWidth(ContinueItemsControl, ContinueScrollViewer);
        SetRowWidth(UpNextItemsControl, UpNextScrollViewer);
        SetRowWidth(FavoritesItemsControl, FavoritesScrollViewer);
        SetRowWidth(RecentlyReleasedMoviesItemsControl, RecentlyReleasedMoviesScrollViewer);
        SetRowWidth(RecentlyReleasedShowsItemsControl, RecentlyReleasedShowsScrollViewer);
        SetRowWidth(RecentlyAddedMoviesItemsControl, RecentlyAddedMoviesScrollViewer);
        SetRowWidth(RecentlyAddedShowsItemsControl, RecentlyAddedShowsScrollViewer);
    }

    private static void SetRowWidth(ItemsControl items, ScrollViewer viewer)
    {
        var count = items.Items.Count;
        if (count == 0)
        {
            items.Width = double.NaN;
            items.HorizontalAlignment = HorizontalAlignment.Left;
            viewer.HorizontalContentAlignment = HorizontalAlignment.Left;
            return;
        }

        // 176px card + 18px margin; keep short rows at natural width.
        var contentWidth = count * 194.0;
        items.Width = contentWidth;
        items.HorizontalAlignment = HorizontalAlignment.Left;
        viewer.HorizontalContentAlignment = HorizontalAlignment.Left;

        items.UpdateLayout();
        viewer.UpdateLayout();

        // Clamp retained offsets when a row shrinks.
        var maxOffset = Math.Max(0, viewer.ExtentWidth - viewer.ViewportWidth);
        if (viewer.HorizontalOffset > maxOffset)
            viewer.ChangeView(maxOffset, null, null, true);
    }

    private static void ScrollRow(ScrollViewer viewer, int direction)
    {
        viewer.UpdateLayout();
        var max = Math.Max(0, viewer.ExtentWidth - viewer.ViewportWidth);
        if (max <= 1) return;

        var amount = Math.Max(240, viewer.ViewportWidth * 0.82);
        var target = Math.Clamp(viewer.HorizontalOffset + amount * direction, 0, max);
        viewer.ChangeView(target, null, null, true);
    }


    private void ReleasedMoviesPreviousButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyReleasedMoviesScrollViewer, -1);
    private void ReleasedMoviesNextButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyReleasedMoviesScrollViewer, 1);
    private void ReleasedShowsPreviousButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyReleasedShowsScrollViewer, -1);
    private void ReleasedShowsNextButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyReleasedShowsScrollViewer, 1);
    private void AddedMoviesPreviousButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyAddedMoviesScrollViewer, -1);
    private void AddedMoviesNextButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyAddedMoviesScrollViewer, 1);
    private void AddedShowsPreviousButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyAddedShowsScrollViewer, -1);
    private void AddedShowsNextButton_Click(object sender, RoutedEventArgs e) => ScrollRow(RecentlyAddedShowsScrollViewer, 1);

    private void MediaCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } && !string.IsNullOrWhiteSpace(item.Id))
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
}
