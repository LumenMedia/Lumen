using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Specialized;
using System.ComponentModel;
using Lumen.Services;
using Lumen.Models;
using Lumen.ViewModels;

namespace Lumen.Views;

public sealed partial class ItemPage : Page
{
    private readonly ItemPageViewModel _viewModel = new();
    private bool _downloadEventsHooked;
    private DownloadButtonState _downloadButtonState = DownloadButtonState.Available;

    private enum DownloadButtonState
    {
        Available,
        Downloading,
        Paused,
        Downloaded
    }

    public ItemPage()
    {
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += ItemPage_Loaded;
        Unloaded += ItemPage_Unloaded;
        AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(ItemPage_PointerWheelChanged), true);
    }

    private void ItemPage_Loaded(object sender, RoutedEventArgs e)
    {
        CastItemsControl.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(CastItemsControl_PointerWheelChanged), true);

        HookDownloadEvents();
        RefreshDownloadButtonState();
    }

    private void ItemPage_Unloaded(object sender, RoutedEventArgs e)
    {
        UnhookDownloadEvents();
    }

    private void HookDownloadEvents()
    {
        if (_downloadEventsHooked)
            return;

        _downloadEventsHooked = true;
        App.Downloads.Items.CollectionChanged += Downloads_CollectionChanged;
        foreach (var entry in App.Downloads.Items)
            entry.PropertyChanged += DownloadEntry_PropertyChanged;
    }

    private void UnhookDownloadEvents()
    {
        if (!_downloadEventsHooked)
            return;

        _downloadEventsHooked = false;
        App.Downloads.Items.CollectionChanged -= Downloads_CollectionChanged;
        foreach (var entry in App.Downloads.Items)
            entry.PropertyChanged -= DownloadEntry_PropertyChanged;
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var entry in e.OldItems.OfType<DownloadEntry>())
                entry.PropertyChanged -= DownloadEntry_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (var entry in e.NewItems.OfType<DownloadEntry>())
                entry.PropertyChanged += DownloadEntry_PropertyChanged;
        }

        DispatcherQueue.TryEnqueue(RefreshDownloadButtonState);
    }

    private void DownloadEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadEntry entry ||
            !string.Equals(entry.ItemId, _viewModel.Id, StringComparison.OrdinalIgnoreCase))
            return;

        if (e.PropertyName is nameof(DownloadEntry.Status) or nameof(DownloadEntry.IsComplete) or nameof(DownloadEntry.LocalPath))
            DispatcherQueue.TryEnqueue(RefreshDownloadButtonState);
    }

    private void RefreshDownloadButtonState()
    {
        if (DownloadButton is null || DownloadButtonText is null || DownloadButtonIcon is null ||
            string.IsNullOrWhiteSpace(_viewModel.Id))
            return;

        var entry = App.Downloads.Items.FirstOrDefault(x =>
            string.Equals(x.ItemId, _viewModel.Id, StringComparison.OrdinalIgnoreCase));

        if (entry is not null && entry.IsComplete && File.Exists(entry.LocalPath))
        {
            _downloadButtonState = DownloadButtonState.Downloaded;
            DownloadButtonText.Text = "Downloaded";
            DownloadButtonIcon.Glyph = "\uE73E";
            ToolTipService.SetToolTip(DownloadButton, "Downloaded • Open Downloads to manage this copy");
        }
        else if (App.Downloads.IsDownloading(_viewModel.Id))
        {
            _downloadButtonState = DownloadButtonState.Downloading;
            DownloadButtonText.Text = "Downloading…";
            DownloadButtonIcon.Glyph = "\uE896";
            ToolTipService.SetToolTip(DownloadButton, "Downloading • Open Downloads to view progress");
        }
        else if (entry is not null && string.Equals(entry.Status, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            _downloadButtonState = DownloadButtonState.Paused;
            DownloadButtonText.Text = "Paused";
            DownloadButtonIcon.Glyph = "\uE769";
            ToolTipService.SetToolTip(DownloadButton, "Download paused • Open Downloads to resume");
        }
        else
        {
            _downloadButtonState = DownloadButtonState.Available;
            DownloadButtonText.Text = "Download";
            DownloadButtonIcon.Glyph = "\uE896";
            ToolTipService.SetToolTip(DownloadButton, "Download for offline playback");
        }

        DownloadButton.IsEnabled = true;
    }

    private void Artwork_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image) return;
        var target = 1.0;
        if (image.Tag is string value &&
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            target = parsed;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(240)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, image);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var requestedSeasonId = e.Parameter is SeriesSeasonNavigation seriesSeason ? seriesSeason.SeasonId : string.Empty;
        var requestedItemId = e.Parameter is SeriesSeasonNavigation nav ? nav.SeriesId : e.Parameter as string;
        if (!string.IsNullOrWhiteSpace(requestedItemId))
        {
            await _viewModel.LoadAsync(requestedItemId);
            await App.Downloads.LoadAsync();
            RefreshDownloadButtonState();
            if (!string.IsNullOrWhiteSpace(requestedSeasonId))
                await _viewModel.SelectSeasonAsync(requestedSeasonId);

            // Reassign after yielding so WinUI refreshes async ComboBox selections.
            await Task.Yield();
            if (_viewModel.SelectedQualityChoice is not null)
                QualityComboBox.SelectedItem = _viewModel.SelectedQualityChoice;
            if (_viewModel.SelectedAudioChoice is not null)
                AudioComboBox.SelectedItem = _viewModel.SelectedAudioChoice;
            if (_viewModel.SelectedSubtitleChoice is not null)
                SubtitleComboBox.SelectedItem = _viewModel.SelectedSubtitleChoice;

            ArtworkPreloader.Preload(
                new[] { _viewModel.ImageUrl, _viewModel.PosterUrl, _viewModel.LogoUrl }
                    .Concat(_viewModel.Cast.Select(x => x.ImageUrl))
                    .Concat(_viewModel.Episodes.Select(x => x.ImageUrl))
                    .Concat(_viewModel.SimilarItems.Select(x => x.ImageUrl)));
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void Play_Click(object sender, RoutedEventArgs e) => Resume_Click(sender, e);

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Id) || !_viewModel.IsPlayable) return;
        Frame.Navigate(typeof(PlayerPage), _viewModel.BuildPlaybackSelection());
    }

    private void PlayFromBeginning_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Id) || !_viewModel.IsPlayable) return;
        Frame.Navigate(typeof(PlayerPage), _viewModel.BuildPlaybackSelection(startFromBeginning: true));
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Id))
            return;

        await App.Downloads.LoadAsync();
        RefreshDownloadButtonState();

        if (_downloadButtonState is DownloadButtonState.Downloaded or DownloadButtonState.Downloading or DownloadButtonState.Paused)
        {
            Frame.Navigate(typeof(DownloadsPage));
            return;
        }

        var button = sender as Button;
        if (button is not null)
            button.IsEnabled = false;

        try
        {
            var item = await App.Jellyfin.GetItemAsync(_viewModel.Id);
            if (item is null)
                return;

            var selectedQuality = DownloadManager.GetQuality(App.Settings.DownloadQuality);
            var optionPanel = new StackPanel { Spacing = 5 };
            var estimateText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenSecondaryTextBrush"],
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            var storagePathText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenMutedBrush"],
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            };

            void UpdateEstimate()
            {
                var estimate = App.Downloads.GetEstimatedBytes(item, selectedQuality.Id);
                var storage = App.Downloads.GetStorageInfo();
                estimateText.Text = estimate > 0
                    ? $"Estimated download size  •  {DownloadManager.FormatBytes(estimate)}"
                    : "Estimated download size  •  Server will determine";
                storagePathText.Text = $"{storage.Summary}\nSave to  •  {storage.Path}";
            }

            foreach (var option in DownloadManager.QualityOptions)
            {
                var radio = new RadioButton
                {
                    GroupName = "LumenDownloadQuality",
                    IsChecked = string.Equals(option.Id, selectedQuality.Id, StringComparison.OrdinalIgnoreCase),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 1, 0, 1)
                };

                var optionCopy = option;
                radio.Checked += (_, _) =>
                {
                    selectedQuality = optionCopy;
                    UpdateEstimate();
                };

                var copy = new StackPanel { Spacing = 2 };
                copy.Children.Add(new TextBlock
                {
                    Text = option.Label,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenTextBrush"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 13
                });
                copy.Children.Add(new TextBlock
                {
                    Text = option.Description,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenMutedBrush"],
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
                radio.Content = copy;
                optionPanel.Children.Add(radio);
            }

            UpdateEstimate();

            var infoCard = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenSubtleSurfaceBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenBorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(13)
            };
            var infoStack = new StackPanel { Spacing = 4 };
            infoStack.Children.Add(estimateText);
            infoStack.Children.Add(storagePathText);
            infoCard.Child = infoStack;

            var content = new StackPanel { Spacing = 12, MinWidth = 500 };
            content.Children.Add(new TextBlock
            {
                Text = "Choose a quality for offline playback.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenSecondaryTextBrush"],
                FontSize = 12
            });
            content.Children.Add(optionPanel);
            content.Children.Add(infoCard);

            var dialog = new ContentDialog
            {
                Title = $"Download {_viewModel.Name}",
                Content = content,
                PrimaryButtonText = "Download",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                _downloadButtonState = DownloadButtonState.Downloading;
                DownloadButtonText.Text = "Downloading…";
                DownloadButtonIcon.Glyph = "\uE896";
                ToolTipService.SetToolTip(DownloadButton, "Downloading • Open Downloads to view progress");
                if (button is not null)
                    button.IsEnabled = true;

                await App.Downloads.DownloadAsync(item, selectedQuality.Id);
                RefreshDownloadButtonState();
            }
            catch (Exception ex)
            {
                var error = new ContentDialog
                {
                    Title = "Download couldn't start",
                    Content = ex.Message,
                    CloseButtonText = "Close",
                    XamlRoot = XamlRoot
                };
                await error.ShowAsync();
            }
        }
        finally
        {
            RefreshDownloadButtonState();
            if (button is not null)
                button.IsEnabled = true;
        }
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.ToggleFavoriteAsync(); } catch { }
    }

    private async void Played_Click(object sender, RoutedEventArgs e)
    {
        try { await _viewModel.TogglePlayedAsync(); } catch { }
    }

    private async void Season_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _viewModel.IsBusy || _viewModel.SelectedSeason is null) return;
        try { await _viewModel.LoadSelectedSeasonAsync(); } catch { }
    }

    private void CastItemsControl_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        => MoveItemPageVertically(e);

    private void ItemPage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.Handled)
            MoveItemPageVertically(e);
    }

    private void MoveItemPageVertically(PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ItemPageScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var nextOffset = Math.Clamp(
            ItemPageScrollViewer.VerticalOffset - delta,
            0,
            Math.Max(0, ItemPageScrollViewer.ScrollableHeight));

        ItemPageScrollViewer.ChangeView(null, nextOffset, null, true);
        e.Handled = true;
    }

    private void CastPrevious_Click(object sender, RoutedEventArgs e) => ScrollCastRow(-1);
    private void CastNext_Click(object sender, RoutedEventArgs e) => ScrollCastRow(1);

    private void ScrollCastRow(int direction)
    {
        CastScrollViewer.UpdateLayout();
        var max = Math.Max(0, CastScrollViewer.ExtentWidth - CastScrollViewer.ViewportWidth);
        if (max <= 1) return;

        var amount = Math.Max(220, CastScrollViewer.ViewportWidth * 0.82);
        var target = Math.Clamp(CastScrollViewer.HorizontalOffset + amount * direction, 0, max);
        CastScrollViewer.ChangeView(target, null, null, true);
    }

    private void Genre_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GenreChoice genre } && !string.IsNullOrWhiteSpace(genre.Name))
            Frame.Navigate(typeof(BrowsePage), new BrowseNavigationParameter(BrowseKind.Genre, genre.Name, genre.Name));
    }

    private void CollectionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } && !string.IsNullOrWhiteSpace(item.Id))
            Frame.Navigate(typeof(ItemPage), item.Id);
    }

    private void Extra_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } && !string.IsNullOrWhiteSpace(item.Id))
            Frame.Navigate(typeof(PlayerPage), item.Id);
    }

    private void CastMember_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CastMemberViewModel person } &&
            !string.IsNullOrWhiteSpace(person.Id))
        {
            Frame.Navigate(typeof(PersonPage), new PersonNavigationParameter(
                person.Id, person.Name, person.ImageUrl));
        }
    }


    private void MediaArtwork_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border artwork)
            return;

        artwork.BorderBrush = Application.Current.Resources["LumenBorderStrongBrush"] as Brush;
        if (artwork.Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border wash && wash.Name == "ArtworkHoverWash")
                {
                    wash.Background = Application.Current.Resources["LumenHoverBrush"] as Brush;
                    break;
                }
            }
        }
    }

    private void MediaArtwork_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border artwork)
            return;

        artwork.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (artwork.Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border wash && wash.Name == "ArtworkHoverWash")
                {
                    wash.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    break;
                }
            }
        }
    }

    private void Episode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } && !string.IsNullOrWhiteSpace(item.Id))
            Frame.Navigate(typeof(ItemPage), item.Id);
    }

    private void Similar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } && !string.IsNullOrWhiteSpace(item.Id))
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

    private void SeriesBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.SeriesId))
            Frame.Navigate(typeof(ItemPage), _viewModel.SeriesId);
    }

    private void SeasonBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.SeriesId) && !string.IsNullOrWhiteSpace(_viewModel.SeasonId))
            Frame.Navigate(typeof(ItemPage), new SeriesSeasonNavigation { SeriesId = _viewModel.SeriesId, SeasonId = _viewModel.SeasonId });
    }

}
