using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Markup;

namespace Lumen.Views;
public sealed partial class MusicPlayerPage : Page
{
    private readonly DispatcherQueueTimer _timer;
    private bool _updatingSlider;
    private string _currentItemId = string.Empty;
    private List<Lumen.Models.LyricLineDto> _timedLyrics = [];
    private int _activeLyricIndex = -1;
    private string _currentTrackName = string.Empty;
    private string _currentArtistName = string.Empty;
    private string _currentAlbumName = string.Empty;
    private string _currentArtistId = string.Empty;
    private double? _currentDurationSeconds;
    private double _lyricsOffsetSeconds;
    private double _lyricsScale = 1.0;
    private CancellationTokenSource? _autoSyncCts;
    public MusicPlayerPage()
    {
        InitializeComponent();
        ShuffleButton.Opacity = .55;
        RepeatButton.Opacity = .55;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += Timer_Tick;
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string id && App.MusicPlayback.CurrentItem?.Id != id)
            await App.MusicPlayback.PlayItemAsync(id);
        await LoadCurrentTrackUiAsync();
        App.MusicPlayback.Changed += MusicPlayback_Changed;
        _timer.Start();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        App.MusicPlayback.Changed -= MusicPlayback_Changed;
        base.OnNavigatedFrom(e);
    }
    private async Task LoadCurrentTrackUiAsync()
    {
        var item = App.MusicPlayback.CurrentItem;
        if (item?.Id is null) return;
        _currentItemId = item.Id;
        _currentTrackName = item.Name ?? string.Empty;
        _currentArtistName = item.Artists?.FirstOrDefault() ?? item.AlbumArtist ?? string.Empty;
        _currentAlbumName = item.Album ?? string.Empty;
        _currentArtistId = item.ArtistItems?.FirstOrDefault()?.Id
            ?? item.AlbumArtists?.FirstOrDefault()?.Id
            ?? string.Empty;
        ArtistButton.IsEnabled = !string.IsNullOrWhiteSpace(_currentArtistId);
        _currentDurationSeconds = item.RunTimeTicks is > 0 ? TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds : null;
        _lyricsOffsetSeconds = App.Settings.LyricOffsets.TryGetValue(_currentItemId, out var savedOffset) ? savedOffset : 0;
        _lyricsScale = App.Settings.LyricScales.TryGetValue(_currentItemId, out var savedScale)
            ? Math.Clamp(savedScale, .985, 1.015)
            : 1.0;
        AutoSyncStatusText.Text = string.Empty;
        UpdateLyricsOffsetUi();
        SetLyricsVisible(true);
        TrackTitle.Text = item.Name ?? "Unknown track";
        ArtistText.Text = item.AlbumArtist ?? item.Artists?.FirstOrDefault() ?? "Unknown artist";
        AlbumText.Text = item.Album ?? string.Empty;
        Artwork.Source = Uri.TryCreate(App.MusicPlayback.ArtworkUrl, UriKind.Absolute, out var imageUri)
            ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(imageUri) : null;
        var lyrics = await App.Jellyfin.GetLyricsAsync(item.Id);
        _timedLyrics = (lyrics?.Lyrics ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        RenderLyrics();
        var hasLyrics = _timedLyrics.Count > 0;
        FindLyricsButton.Visibility = hasLyrics ? Visibility.Collapsed : Visibility.Visible;
        ReplaceLyricsButton.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
        AutoSyncLyricsButton.IsEnabled = _timedLyrics.Count(x => x.Start.HasValue) >= 3;
        UpdateTransportUi();
    }

    private void MusicPlayback_Changed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (App.MusicPlayback.CurrentItem?.Id != _currentItemId) await LoadCurrentTrackUiAsync();
            else UpdateTransportUi();
        });
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => App.MusicPlayback.PlayPause();
    private async void Previous_Click(object sender, RoutedEventArgs e) => await App.MusicPlayback.PreviousAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await App.MusicPlayback.NextAsync();
    private void Shuffle_Click(object sender, RoutedEventArgs e) { App.MusicPlayback.ToggleShuffle(); UpdateTransportUi(); }
    private void Repeat_Click(object sender, RoutedEventArgs e) { App.MusicPlayback.CycleRepeat(); UpdateTransportUi(); }

    private void UpdateTransportUi()
    {
        PlayPauseIcon.Glyph = App.MusicPlayback.IsPlaying ? "\uE769" : "\uE768";
        ShuffleButton.Opacity = App.MusicPlayback.ShuffleEnabled ? 1 : .55;
        ShuffleButton.Background = App.MusicPlayback.ShuffleEnabled ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(56,149,103,232)) : null;
        RepeatButton.Opacity = App.MusicPlayback.RepeatMode == Lumen.Services.MusicRepeatMode.Off ? .55 : 1;
        RepeatButton.Background = App.MusicPlayback.RepeatMode == Lumen.Services.MusicRepeatMode.Off ? null : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(56,149,103,232));
        RepeatIcon.Glyph = App.MusicPlayback.RepeatMode == Lumen.Services.MusicRepeatMode.One ? "\uE8ED" : "\uE8EE";
        ToolTipService.SetToolTip(RepeatButton, App.MusicPlayback.RepeatMode switch { Lumen.Services.MusicRepeatMode.One => "Repeat one", Lumen.Services.MusicRepeatMode.All => "Repeat all", _ => "Repeat off" });
    }

    private void Artist_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentArtistId))
            Frame.Navigate(typeof(ArtistPage), _currentArtistId);
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        var session = App.MusicPlayback.Player.PlaybackSession;
        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, session.NaturalDuration.TotalSeconds);
        PositionSlider.Value = Math.Clamp(session.Position.TotalSeconds, 0, PositionSlider.Maximum);
        PositionText.Text = Format(session.Position); DurationText.Text = Format(session.NaturalDuration);
        _updatingSlider = false;
        UpdateActiveLyric(GetLyricClockTicks(session.Position));
    }

    private void RenderLyrics()
    {
        _activeLyricIndex = -1;
        LyricsItems.Items.Clear();

        if (_timedLyrics.Count == 0)
        {
            LyricsItems.Items.Add(new TextBlock
            {
                Text = "No lyrics are available for this track.",
                FontSize = 20,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255,199,192,207)),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var line in _timedLyrics)
        {
            var text = new TextBlock
            {
                Text = line.Text,
                FontSize = 22,
                LineHeight = 34,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255,199,192,207)),
                Opacity = .50,
                TextWrapping = TextWrapping.Wrap
            };

            if (line.Start.HasValue)
            {
                var row = new Button
                {
                    Tag = line.Start.Value,
                    Content = text,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0,0,0,0)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0,5,0,5),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    CornerRadius = new CornerRadius(8)
                };

                ToolTipService.SetToolTip(row, "Play from this lyric");
                row.Click += LyricLine_Click;
                row.PointerEntered += (_, _) =>
                {
                    if (text != GetActiveLyricTextBlock())
                        text.Opacity = .82;
                };
                row.PointerExited += (_, _) =>
                {
                    if (text != GetActiveLyricTextBlock())
                        text.Opacity = GetLyricTextIndex(text) < _activeLyricIndex ? .30 : .50;
                };
                LyricsItems.Items.Add(row);
            }
            else
            {
                text.Margin = new Thickness(0,5,0,5);
                LyricsItems.Items.Add(text);
            }
        }
    }

    private async void LyricLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button row || row.Tag is not long startTicks)
            return;

        var target = GetPlaybackTimeForLyricStart(startTicks);
        App.MusicPlayback.Seek(target);
        UpdateActiveLyric(GetLyricClockTicks(target), true);
        await Task.CompletedTask;
    }

    private TextBlock? GetActiveLyricTextBlock()
        => _activeLyricIndex >= 0 && _activeLyricIndex < LyricsItems.Items.Count
            ? GetLyricTextBlock(LyricsItems.Items[_activeLyricIndex])
            : null;

    private int GetLyricTextIndex(TextBlock target)
    {
        for (var i = 0; i < LyricsItems.Items.Count; i++)
            if (ReferenceEquals(GetLyricTextBlock(LyricsItems.Items[i]), target))
                return i;
        return -1;
    }

    private static TextBlock? GetLyricTextBlock(object item)
        => item switch
        {
            TextBlock text => text,
            Button { Content: TextBlock text } => text,
            _ => null
        };

    private void UpdateActiveLyric(long ticks, bool forceScroll = false)
    {
        if (_timedLyrics.Count == 0) return;

        var index = -1;
        for (var i = 0; i < _timedLyrics.Count; i++)
        {
            var start = _timedLyrics[i].Start;
            if (start is null) continue;
            if (start.Value <= ticks) index = i;
            else break;
        }

        if (index == _activeLyricIndex && !forceScroll) return;
        _activeLyricIndex = index;

        for (var i = 0; i < LyricsItems.Items.Count; i++)
        {
            var line = GetLyricTextBlock(LyricsItems.Items[i]);
            if (line is null) continue;

            var active = i == index;
            line.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                active ? Windows.UI.Color.FromArgb(255,255,255,255)
                       : Windows.UI.Color.FromArgb(255,199,192,207));
            line.Opacity = active ? 1 : (i < index ? .30 : .50);
            line.FontWeight = active
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        if (index >= 0 && index < LyricsItems.Items.Count &&
            LyricsItems.Items[index] is FrameworkElement lyricElement)
        {
            lyricElement.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = !forceScroll,
                VerticalAlignmentRatio = .35
            });
        }
    }

    private void PositionSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider) return;
        var target = TimeSpan.FromSeconds(e.NewValue);
        App.MusicPlayback.Seek(target);
        UpdateActiveLyric(GetLyricClockTicks(target), true);
    }
    private static string Format(TimeSpan v) => v.TotalHours >= 1 ? v.ToString(@"h\:mm\:ss") : v.ToString(@"m\:ss");
    private async void FindLyrics_Click(object sender, RoutedEventArgs e) => await SearchLyricsAsync(false);
    private async void ReplaceLyrics_Click(object sender, RoutedEventArgs e) => await SearchLyricsAsync(true);

    private async Task SearchLyricsAsync(bool replace)
    {
        if (string.IsNullOrWhiteSpace(_currentItemId)) return;
        FindLyricsButton.IsEnabled = ReplaceLyricsButton.IsEnabled = false;
        try
        {
            var results = await App.LrcLib.SearchAsync(
                _currentTrackName, _currentArtistName, _currentAlbumName, _currentDurationSeconds);

            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                DisplayMemberPath = "Label",
                SelectedIndex = 0,
                MinWidth = 620,
                MaxHeight = 360
            };

            var searchBox = new TextBox
            {
                Header = "Manual search",
                PlaceholderText = "Song title, original artist, or any keywords",
                Text = _currentTrackName,
                MinWidth = 500
            };
            var searchButton = new Button
            {
                Content = "Search",
                Padding = new Thickness(18, 8, 18, 8),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var searchStatus = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 199, 192, 207)),
                TextWrapping = TextWrapping.Wrap
            };

            void PopulateChoices(List<Lumen.Models.LrcLibResultDto> source)
            {
                var choices = source.Select(x => new LrcChoice
                {
                    Result = x,
                    Label = $"{x.TrackName} — {x.ArtistName}" +
                            (!string.IsNullOrWhiteSpace(x.AlbumName) ? $" • {x.AlbumName}" : "") +
                            (x.Duration is > 0 ? $" • {TimeSpan.FromSeconds(x.Duration.Value).ToString(@"m\:ss")}" : "") +
                            (!string.IsNullOrWhiteSpace(x.SyncedLyrics) ? " • Synced" : " • Plain")
                }).ToList();

                list.ItemsSource = choices;
                list.SelectedIndex = choices.Count > 0 ? 0 : -1;
                searchStatus.Text = choices.Count == 0
                    ? "No matches. Try the original song title or original artist."
                    : $"{choices.Count} result{(choices.Count == 1 ? "" : "s")}";
            }

            PopulateChoices(results);

            async Task RunManualSearchAsync()
            {
                var query = searchBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(query)) return;

                searchBox.IsEnabled = false;
                searchButton.IsEnabled = false;
                searchStatus.Text = "Searching LRCLIB…";
                try
                {
                    var manualResults = await App.LrcLib.ManualSearchAsync(query, _currentDurationSeconds);
                    PopulateChoices(manualResults);
                }
                catch (Exception ex)
                {
                    searchStatus.Text = $"Search failed: {ex.Message}";
                }
                finally
                {
                    searchBox.IsEnabled = true;
                    searchButton.IsEnabled = true;
                }
            }

            searchButton.Click += async (_, _) => await RunManualSearchAsync();

            searchBox.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter && searchButton.IsEnabled)
                {
                    args.Handled = true;
                    await RunManualSearchAsync();
                }
            };

            var searchRow = new Grid { ColumnSpacing = 10 };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(searchBox, 0);
            Grid.SetColumn(searchButton, 1);
            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(searchButton);

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(searchRow);
            content.Children.Add(searchStatus);
            content.Children.Add(list);

            var dialog = new ContentDialog
            {
                Title = replace ? "Replace lyrics" : "Find lyrics",
                Content = content,
                PrimaryButtonText = replace ? "Use selected lyrics" : "Download selected lyrics",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
                list.SelectedItem is not LrcChoice choice)
                return;

            var lyricContent = !string.IsNullOrWhiteSpace(choice.Result.SyncedLyrics)
                ? choice.Result.SyncedLyrics!
                : choice.Result.PlainLyrics!;
            var extension = !string.IsNullOrWhiteSpace(choice.Result.SyncedLyrics) ? ".lrc" : ".txt";

            if (replace)
            {
                try { await App.Jellyfin.DeleteLyricsAsync(_currentItemId); }
                catch { }
            }

            await App.Jellyfin.UploadLyricsAsync(_currentItemId, $"lyrics{extension}", lyricContent);
            var lyrics = await App.Jellyfin.GetLyricsAsync(_currentItemId);
            _timedLyrics = (lyrics?.Lyrics ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
            RenderLyrics();

            var hasLyrics = _timedLyrics.Count > 0;
            FindLyricsButton.Visibility = hasLyrics ? Visibility.Collapsed : Visibility.Visible;
            ReplaceLyricsButton.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
            AutoSyncLyricsButton.IsEnabled = _timedLyrics.Count(x => x.Start.HasValue) >= 3;
            SetLyricsVisible(true);
        }
        catch (Exception ex)
        {
            var d = new ContentDialog
            {
                Title = "Lyrics",
                Content = $"Could not fetch/save lyrics: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await d.ShowAsync();
        }
        finally
        {
            FindLyricsButton.IsEnabled = true;
            ReplaceLyricsButton.IsEnabled = true;
        }
    }

    private sealed class LrcChoice
    {
        public string Label { get; set; } = string.Empty;
        public Lumen.Models.LrcLibResultDto Result { get; set; } = new();
    }

    private void ToggleLyrics_Click(object sender, RoutedEventArgs e)
        => SetLyricsVisible(LyricsPanel.Visibility != Visibility.Visible);

    private void SetLyricsVisible(bool visible)
    {
        LyricsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LyricsColumn.Width = visible ? new GridLength(0.92, GridUnitType.Star) : new GridLength(0);
        NowPlayingGrid.ColumnSpacing = visible ? 46 : 0;
        LyricsToggleButton.Content = visible ? "Hide lyrics" : "Show lyrics";

        // Let artwork use the lyrics column when lyrics are hidden.
        var artSize = visible ? 480d : 660d;
        ArtworkCard.Width = artSize;
        ArtworkCard.Height = artSize;
        TransportControls.Width = artSize;

        if (ArtworkPane.Children.FirstOrDefault() is StackPanel stack)
        {
            foreach (var child in stack.Children)
            {
                if (child is StackPanel metadata)
                    metadata.Width = artSize;
                else if (child is Grid progress)
                    progress.Width = artSize;
            }
        }
    }

    private async void LyricsOffsetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item ||
            !double.TryParse(
                item.Tag?.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var delta))
            return;

        await AdjustLyricsOffsetAsync(delta);
    }

    private async void LyricsOffsetReset_Click(object sender, RoutedEventArgs e)
    {
        _lyricsOffsetSeconds = 0;
        _lyricsScale = 1.0;
        AutoSyncStatusText.Text = string.Empty;
        await SaveLyricsSyncAsync();
    }

    private async Task AdjustLyricsOffsetAsync(double delta)
    {
        _lyricsOffsetSeconds = Math.Clamp(_lyricsOffsetSeconds + delta, -30, 30);
        AutoSyncStatusText.Text = string.Empty;
        await SaveLyricsSyncAsync();
    }

    private async Task SaveLyricsSyncAsync()
    {
        if (!string.IsNullOrWhiteSpace(_currentItemId))
        {
            if (Math.Abs(_lyricsOffsetSeconds) < .001)
                App.Settings.LyricOffsets.Remove(_currentItemId);
            else
                App.Settings.LyricOffsets[_currentItemId] = _lyricsOffsetSeconds;

            if (Math.Abs(_lyricsScale - 1.0) < .000001)
                App.Settings.LyricScales.Remove(_currentItemId);
            else
                App.Settings.LyricScales[_currentItemId] = _lyricsScale;

            await App.Settings.SaveAsync();
        }

        UpdateLyricsOffsetUi();
        UpdateActiveLyric(GetLyricClockTicks(App.MusicPlayback.Player.PlaybackSession.Position), true);
    }

    private long GetLyricClockTicks(TimeSpan playbackPosition)
    {
        var seconds = (playbackPosition.TotalSeconds - _lyricsOffsetSeconds) /
                      Math.Max(.001, _lyricsScale);
        return TimeSpan.FromSeconds(seconds).Ticks;
    }

    private TimeSpan GetPlaybackTimeForLyricStart(long lyricStartTicks)
    {
        var lyricSeconds = TimeSpan.FromTicks(lyricStartTicks).TotalSeconds;
        var playbackSeconds = lyricSeconds * _lyricsScale + _lyricsOffsetSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, playbackSeconds));
    }

    private void UpdateLyricsOffsetUi()
    {
        var offsetText = $"{_lyricsOffsetSeconds:+0.0;-0.0;0.0}s";
        var scaleText = Math.Abs(_lyricsScale - 1.0) > .00005
            ? $" • {_lyricsScale:0.0000}×"
            : string.Empty;
        LyricsOffsetStatusMenuItem.Text = $"Current sync: {offsetText}{scaleText}";
        ToolTipService.SetToolTip(
            LyricsOptionsButton,
            $"Lyric sync options — {offsetText}{scaleText}");
    }

    private async void AutoSyncLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentItemId) ||
            _timedLyrics.Count(x => x.Start.HasValue) < 3)
            return;

        _autoSyncCts?.Cancel();
        _autoSyncCts?.Dispose();
        _autoSyncCts = new CancellationTokenSource();

        AutoSyncLyricsButton.IsEnabled = false;
        LyricsOptionsButton.IsEnabled = false;

        var progress = new Progress<string>(message =>
        {
            AutoSyncStatusText.Text = message;
        });

        try
        {
            var result = await App.LyricsAutoSync.SyncAsync(
                _currentItemId,
                _timedLyrics,
                progress,
                _autoSyncCts.Token);

            _lyricsOffsetSeconds = result.OffsetSeconds;
            _lyricsScale = result.Scale;
            await SaveLyricsSyncAsync();

            var drift = Math.Abs(_lyricsScale - 1.0) > .00005
                ? $" • {_lyricsScale:0.0000}×"
                : string.Empty;
            AutoSyncStatusText.Text =
                $"Synced {_lyricsOffsetSeconds:+0.0;-0.0;0.0}s{drift} • {result.MatchCount} matches";

            var position = App.MusicPlayback.Player.PlaybackSession.Position;
            UpdateActiveLyric(GetLyricClockTicks(position), true);
        }
        catch (OperationCanceledException)
        {
            AutoSyncStatusText.Text = "Auto Sync cancelled";
        }
        catch (Exception ex)
        {
            AutoSyncStatusText.Text = "Auto Sync failed";
            var dialog = new ContentDialog
            {
                Title = "Auto Sync",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            AutoSyncLyricsButton.IsEnabled = _timedLyrics.Count(x => x.Start.HasValue) >= 3;
            LyricsOptionsButton.IsEnabled = true;
        }
    }

    public void PrepareForAppShutdown()
    {
        _timer.Stop();
        try { _autoSyncCts?.Cancel(); } catch { }
        _autoSyncCts?.Dispose();
        _autoSyncCts = null;
    }

}