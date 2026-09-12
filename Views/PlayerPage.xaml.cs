using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Lumen.Models;
using Lumen.ViewModels;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using Windows.Foundation.Collections;
using Windows.Gaming.Input;

namespace Lumen.Views;

public sealed partial class PlayerPage : Page
{
    private readonly PlayerViewModel _viewModel = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _controlsHideTimer;
    private bool _playbackReported;
    private bool _stopReported;
    private DateTime _lastProgressReportUtc;
    private PlaybackSelection? _selection;
    private long _pendingSeekTicks;
    private bool _sourceOpening;
    private MediaPlaybackItem? _playbackItem;
    private bool _audioFallbackAttempted;
    private bool _updatingSeekSlider;
    private bool _userSeeking;
    private long _streamBasePositionTicks;
    private int _sourceGeneration;
    private double _requestedSeekSeconds = double.NaN;
    private bool _controlsVisible = true;
    private MediaPlaybackState? _lastObservedPlaybackState;
    private bool _updatingVolumeUi;
    private bool _isShuttingDown;
    private readonly DispatcherQueueTimer _upNextTimer;
    private readonly DispatcherQueueTimer _gamepadTimer;
    private GamepadButtons _lastGamepadButtons;
    private int _upNextSeconds;
    private bool _statsRefreshRunning;
    private DateTime _lastStatsRefreshUtc = DateTime.MinValue;
    private bool _syncPlayJoined;
    private readonly List<MediaSegmentDto> _mediaSegments = [];
    private readonly HashSet<string> _handledSegments = new(StringComparer.OrdinalIgnoreCase);
    private MediaSegmentDto? _activeSegment;
    private readonly Microsoft.UI.Input.InputCursor _arrowCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
    private readonly Lumen.Controls.TransparentCursorHandle _transparentCursor =
        Lumen.Controls.TransparentCursorHandle.Create();

    public PlayerPage()
    {
        InitializeComponent();

        DataContext = _viewModel;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += Timer_Tick;

        _controlsHideTimer = DispatcherQueue.CreateTimer();
        _controlsHideTimer.Interval = TimeSpan.FromSeconds(3);
        _controlsHideTimer.IsRepeating = false;
        _controlsHideTimer.Tick += ControlsHideTimer_Tick;

        _upNextTimer = DispatcherQueue.CreateTimer();
        _upNextTimer.Interval = TimeSpan.FromSeconds(1);
        _upNextTimer.IsRepeating = true;
        _upNextTimer.Tick += UpNextTimer_Tick;

        _gamepadTimer = DispatcherQueue.CreateTimer();
        _gamepadTimer.Interval = TimeSpan.FromMilliseconds(100);
        _gamepadTimer.IsRepeating = true;
        _gamepadTimer.Tick += GamepadTimer_Tick;
        if (App.Settings.GamepadEnabled)
            _gamepadTimer.Start();

        // Listen through handled Thumb events so seek dragging stays reliable.
        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SeekSlider_PointerPressed), true);
        SeekSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SeekSlider_PointerReleased), true);

        PlayerRoot.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PlayerRoot_PointerActivity), true);
        PlayerRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PlayerRoot_PointerActivity), true);

        // Set volume after handlers exist because Slider.Value can raise ValueChanged.
        _updatingVolumeUi = true;
        VolumeSlider.Value = Player.MediaPlayer?.Volume * 100.0 ?? 100.0;
        _updatingVolumeUi = false;
        UpdateVolumeGlyph();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.MusicPlayback.PauseForVideo();
        _selection = e.Parameter switch
        {
            PlaybackSelection value => value,
            string id => new PlaybackSelection { ItemId = id },
            _ => null
        };
        if (_selection is null) return;
        _audioFallbackAttempted = false;
        await _viewModel.LoadAsync(_selection);

        _mediaSegments.Clear();
        _handledSegments.Clear();
        try
        {
            var segments = await App.Jellyfin.GetMediaSegmentsAsync(_viewModel.ItemId);
            _mediaSegments.AddRange(segments.Items
                .Where(x => x.EndTicks > x.StartTicks)
                .OrderBy(x => x.StartTicks));
        }
        catch { }

        if (string.IsNullOrWhiteSpace(_viewModel.StreamUrl))
        {
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }
        await StartSourceAsync();
    }

    private async Task StartSourceAsync()
    {
        var generation = ++_sourceGeneration;
        try
        {
            Player.MediaPlayer.MediaOpened -= MediaOpened;
            Player.MediaPlayer.MediaFailed -= MediaFailed;
            Player.MediaPlayer.MediaEnded -= MediaEnded;
            Player.MediaPlayer.MediaOpened += MediaOpened;
            Player.MediaPlayer.MediaFailed += MediaFailed;
            Player.MediaPlayer.MediaEnded += MediaEnded;
            _playbackReported = false;
            _stopReported = false;
            _sourceOpening = true;

            if (_playbackItem is not null)
            {
                _playbackItem.AudioTracksChanged -= PlaybackItem_AudioTracksChanged;
                _playbackItem.TimedMetadataTracksChanged -= PlaybackItem_TimedMetadataTracksChanged;
            }

            // Fully detach the previous source before renegotiation.
            try { Player.MediaPlayer.Pause(); } catch { }
            Player.Source = null;
            _playbackItem = null;

            // Yield so a detached HLS source cannot win a late open race.
            await Task.Yield();
            if (generation != _sourceGeneration) return;

            var uri = new Uri(_viewModel.StreamUrl);
            MediaSource mediaSource;

            if (_viewModel.IsAdaptiveStream || IsAdaptiveUri(uri))
            {
                var result = await AdaptiveMediaSource.CreateFromUriAsync(uri);
                if (result.Status != AdaptiveMediaSourceCreationStatus.Success || result.MediaSource is null)
                {
                    throw new InvalidOperationException(
                        $"Windows could not open the Jellyfin adaptive stream ({result.Status}).");
                }

                mediaSource = MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
            }
            else
            {
                mediaSource = MediaSource.CreateFromUri(uri);
            }

            if (generation != _sourceGeneration) return;

            _playbackItem = new MediaPlaybackItem(mediaSource);
            _playbackItem.AudioTracksChanged += PlaybackItem_AudioTracksChanged;
            _playbackItem.TimedMetadataTracksChanged += PlaybackItem_TimedMetadataTracksChanged;
            ApplySubtitlePresentationMode(_playbackItem);

            try
            {
                var display = _playbackItem.GetDisplayProperties();
                display.Type = MediaPlaybackType.Video;
                display.VideoProperties.Title = _viewModel.Title;
                display.VideoProperties.Subtitle = string.IsNullOrWhiteSpace(_viewModel.SeriesName)
                    ? _viewModel.MediaDetailsLabel
                    : _viewModel.SeriesName;
                if (!string.IsNullOrWhiteSpace(_viewModel.ArtworkUrl))
                    display.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(_viewModel.ArtworkUrl));
                _playbackItem.ApplyDisplayProperties(display);
                Player.MediaPlayer.CommandManager.IsEnabled = true;
            }
            catch { }

            Player.Source = _playbackItem;
            _timer.Start();
        }
        catch (Exception ex)
        {
            _sourceOpening = false;
            _viewModel.ErrorMessage = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void PlaybackItem_TimedMetadataTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() => ApplySubtitlePresentationMode(sender));
    }

    private void ApplySubtitlePresentationMode(MediaPlaybackItem item)
    {
        // Null means subtitles are explicitly off.
        if (_viewModel.SelectedSubtitleIndex is not null)
            return;

        try
        {
            for (uint i = 0; i < item.TimedMetadataTracks.Count; i++)
                item.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
        }
        catch
        {
        }
    }

    private static bool IsAdaptiveUri(Uri uri)
    {
        var path = uri.AbsolutePath;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Contains("m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private void MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Ignore MediaOpened from a source detached during a seek.
            if (_playbackItem is null || !ReferenceEquals(Player.Source, _playbackItem)) return;
            var authoritativeDuration = GetAuthoritativeDuration(sender.PlaybackSession);
            SeekSlider.Maximum = Math.Max(1, authoritativeDuration.TotalSeconds);
            DurationText.Text = Format(authoritativeDuration);
            UpdateChapterMarkers();
            if (_playbackItem is not null)
                ApplySubtitlePresentationMode(_playbackItem);

            try
            {
                sender.PlaybackSession.PlaybackRate = 1.0;
                SpeedGlyph.Text = "1×";
            }
            catch { }

            // HLS/transcode timelines start at the negotiated StartTimeTicks offset.
            _streamBasePositionTicks = string.Equals(_viewModel.CurrentPlayMethod, "DirectPlay", StringComparison.OrdinalIgnoreCase)
                ? 0
                : Math.Max(0, _viewModel.StreamStartPositionTicks);

            var targetTicks = _pendingSeekTicks > 0 ? _pendingSeekTicks : _viewModel.ResumePositionTicks;
            var shouldResumeDirectPlay =
                string.Equals(_viewModel.CurrentPlayMethod, "DirectPlay", StringComparison.OrdinalIgnoreCase) &&
                targetTicks > 0;

            if (shouldResumeDirectPlay)
            {
                try
                {
                    sender.PlaybackSession.Position = TimeSpan.FromTicks(targetTicks);
                }
                catch { }
            }

            var logicalPosition = shouldResumeDirectPlay
                ? TimeSpan.FromTicks(targetTicks)
                : GetLogicalPosition(sender.PlaybackSession);
            _updatingSeekSlider = true;
            SeekSlider.Value = Math.Clamp(logicalPosition.TotalSeconds, 0, SeekSlider.Maximum);
            _updatingSeekSlider = false;
            CurrentTimeText.Text = Format(logicalPosition);

            _pendingSeekTicks = 0;
            _sourceOpening = false;
            _ = ValidateSelectedAudioTrackAsync();
            sender.Play();

            // Reapply Direct Play resume after Opened→Playing if Media Foundation resets it.
            if (shouldResumeDirectPlay)
            {
                try
                {
                    sender.PlaybackSession.Position = TimeSpan.FromTicks(targetTicks);
                }
                catch { }
            }

            _ = ReportPlaybackStartAsync();
            UpdatePlayPause();
            ShowPlayerControls();
        });
    }

    private void PlaybackItem_AudioTracksChanged(MediaPlaybackItem sender, IVectorChangedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() => _ = ValidateSelectedAudioTrackAsync());
    }

    private async Task ValidateSelectedAudioTrackAsync()
    {
        if (_playbackItem is null || _selection is null) return;
        if (!string.Equals(_viewModel.CurrentPlayMethod, "DirectPlay", StringComparison.OrdinalIgnoreCase)) return;

        var ordinal = _viewModel.SelectedAudioOrdinal;
        if (ordinal < 0 || ordinal >= _playbackItem.AudioTracks.Count) return;

        var track = _playbackItem.AudioTracks[ordinal];
        try
        {
            // PlayerViewModel maps Jellyfin stream indexes to audio-track ordinals.
            _playbackItem.AudioTracks.SelectedIndex = ordinal;
        }
        catch
        {
            await RenegotiateForUnsupportedAudioAsync();
            return;
        }

        try
        {
            track.OpenFailed -= SelectedAudioTrack_OpenFailed;
            track.OpenFailed += SelectedAudioTrack_OpenFailed;

            // Validate the actual selected track/container with Media Foundation.
            if (track.SupportInfo.DecoderStatus != MediaDecoderStatus.FullySupported)
                await RenegotiateForUnsupportedAudioAsync();
        }
        catch
        {
            await RenegotiateForUnsupportedAudioAsync();
        }
    }

    private void SelectedAudioTrack_OpenFailed(AudioTrack sender, AudioTrackOpenFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() => _ = RenegotiateForUnsupportedAudioAsync());
    }

    private async Task RenegotiateForUnsupportedAudioAsync()
    {
        if (_audioFallbackAttempted || _selection is null) return;
        if (!string.Equals(_viewModel.CurrentPlayMethod, "DirectPlay", StringComparison.OrdinalIgnoreCase)) return;
        _audioFallbackAttempted = true;

        var currentTicks = Math.Max(0, Player.MediaPlayer.PlaybackSession.Position.Ticks);
        if (currentTicks == 0) currentTicks = Math.Max(_pendingSeekTicks, _viewModel.ResumePositionTicks);
        _pendingSeekTicks = currentTicks;
        _viewModel.ResumePositionTicks = currentTicks;

        await ReportPlaybackStoppedAsync();
        try
        {
            // Renegotiate unsupported audio without forcing a full video transcode.
            await _viewModel.NegotiateAsync(
                _selection,
                currentTicks,
                default,
                enableDirectPlay: false,
                allowAudioStreamCopy: false);

            ErrorPanel.Visibility = Visibility.Collapsed;
            await StartSourceAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _viewModel.ErrorMessage = string.IsNullOrWhiteSpace(args.ErrorMessage)
                ? "The negotiated Jellyfin stream could not be played."
                : args.ErrorMessage;
            ErrorPanel.Visibility = Visibility.Visible;
        });
    }

    private void PlayerRoot_PointerActivity(object sender, PointerRoutedEventArgs e)
    {
        ShowPlayerControls();
    }

    private void ShowPlayerControls()
    {
        ShowMouseCursor();
        if (!_controlsVisible)
        {
            TopChrome.Visibility = Visibility.Visible;
            ControlsOverlay.Visibility = Visibility.Visible;
            _controlsVisible = true;
        }

        RestartControlsHideTimer();
    }

    private void RestartControlsHideTimer()
    {
        if (_controlsHideTimer is null || Player?.MediaPlayer?.PlaybackSession is not { } session)
            return;

        _controlsHideTimer.Stop();

        var menuOpen = AudioPanel?.Visibility == Visibility.Visible ||
            SubtitlePanel?.Visibility == Visibility.Visible ||
            SpeedPanel?.Visibility == Visibility.Visible ||
            ChaptersPanel?.Visibility == Visibility.Visible ||
            SyncPlayPanel?.Visibility == Visibility.Visible ||
            DiagnosticsPanel?.Visibility == Visibility.Visible ||
            CreditsNextOverlay?.Visibility == Visibility.Visible ||
            UpNextPanel?.Visibility == Visibility.Visible;

        // Hide idle chrome only while video is playing.
        if (session.PlaybackState == MediaPlaybackState.Playing &&
            !_userSeeking &&
            !menuOpen &&
            ErrorPanel?.Visibility != Visibility.Visible)
        {
            _controlsHideTimer.Start();
        }
    }

    private void ControlsHideTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isShuttingDown) return;

        MediaPlaybackSession session;
        try
        {
            var mediaPlayer = Player?.MediaPlayer;
            if (mediaPlayer is null) return;
            session = mediaPlayer.PlaybackSession;
        }
        catch (COMException)
        {
            return;
        }

        var menuOpen = AudioPanel?.Visibility == Visibility.Visible ||
            SubtitlePanel?.Visibility == Visibility.Visible ||
            SpeedPanel?.Visibility == Visibility.Visible ||
            ChaptersPanel?.Visibility == Visibility.Visible ||
            SyncPlayPanel?.Visibility == Visibility.Visible ||
            DiagnosticsPanel?.Visibility == Visibility.Visible ||
            CreditsNextOverlay?.Visibility == Visibility.Visible ||
            UpNextPanel?.Visibility == Visibility.Visible;

        if (session.PlaybackState != MediaPlaybackState.Playing ||
            _userSeeking ||
            menuOpen ||
            ErrorPanel?.Visibility == Visibility.Visible)
            return;

        TopChrome.Visibility = Visibility.Collapsed;
        ControlsOverlay.Visibility = Visibility.Collapsed;
        HideMouseCursor();
        _controlsVisible = false;
    }

    private void HideMouseCursor()
    {
        if (_isShuttingDown) return;

        var cursor = _transparentCursor.Cursor;
        if (cursor is not null)
        {
            PlayerRoot.InputCursor = cursor;
            PlayerInputSurface.InputCursor = cursor;
        }

        // WinUI can reset ProtectedCursor, so also apply the native transparent cursor.
        _transparentCursor.ApplyNative();
    }

    private void ShowMouseCursor()
    {
        if (_isShuttingDown) return;
        PlayerRoot.InputCursor = _arrowCursor;
        PlayerInputSurface.InputCursor = _arrowCursor;
        Lumen.Controls.TransparentCursorHandle.RestoreSystemArrow();
    }

    private void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_sourceOpening || _isShuttingDown) return;

        MediaPlaybackSession session;
        try
        {
            var mediaPlayer = Player?.MediaPlayer;
            if (mediaPlayer is null) return;
            session = mediaPlayer.PlaybackSession;
        }
        catch (COMException)
        {
            return;
        }
        if (_lastObservedPlaybackState != session.PlaybackState)
        {
            _lastObservedPlaybackState = session.PlaybackState;
            HandlePlaybackStateChanged(session.PlaybackState);
        }

        var duration = GetAuthoritativeDuration(session);
        if (duration > TimeSpan.Zero)
        {
            SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            if (!_userSeeking)
            {
                var logicalPosition = GetLogicalPosition(session);
                _updatingSeekSlider = true;
                SeekSlider.Value = Math.Clamp(logicalPosition.TotalSeconds, 0, SeekSlider.Maximum);
                _updatingSeekSlider = false;
                CurrentTimeText.Text = Format(logicalPosition);
            }
            DurationText.Text = Format(duration);
        }

        if (_playbackReported && DateTime.UtcNow - _lastProgressReportUtc >= TimeSpan.FromSeconds(5))
        {
            _lastProgressReportUtc = DateTime.UtcNow;
            _ = ReportPlaybackProgressAsync();
        }

        UpdateSegmentUi();

        BufferingIndicator.Visibility = session.PlaybackState == MediaPlaybackState.Buffering
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Reassert the hidden cursor if Windows restores it during playback.
        if (!_controlsVisible &&
            session.PlaybackState == MediaPlaybackState.Playing &&
            !_userSeeking)
        {
            HideMouseCursor();
        }

        if (DiagnosticsPanel?.Visibility == Visibility.Visible)
        {
            var pos = GetLogicalPosition(session);
            DiagnosticsPositionText.Text =
                $"Position: {Format(pos)} / {Format(GetAuthoritativeDuration(session))}  •  State: {session.PlaybackState}";

            if (!_statsRefreshRunning && DateTime.UtcNow - _lastStatsRefreshUtc >= TimeSpan.FromSeconds(4))
                _ = RefreshServerPlaybackStatsAsync();
        }

        UpdatePlayPause();
    }

    private void SeekSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateChapterMarkers();

    private void UpdateChapterMarkers()
    {
        if (ChapterMarkersCanvas is null || SeekSlider is null)
            return;

        ChapterMarkersCanvas.Children.Clear();
        var width = ChapterMarkersCanvas.ActualWidth;
        var durationSeconds = SeekSlider.Maximum;
        if (width <= 1 || durationSeconds <= 1 || _viewModel.Chapters.Count == 0)
            return;

        foreach (var chapter in _viewModel.Chapters)
        {
            var seconds = TimeSpan.FromTicks(chapter.StartPositionTicks).TotalSeconds;
            if (seconds <= 0 || seconds >= durationSeconds)
                continue;

            var marker = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = 2,
                Height = 7,
                RadiusX = 1,
                RadiusY = 1,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(190, 255, 255, 255)),
                Opacity = 0.72
            };
            Canvas.SetLeft(marker, Math.Clamp((seconds / durationSeconds) * width - 1, 0, Math.Max(0, width - 2)));
            Canvas.SetTop(marker, 1.5);
            ChapterMarkersCanvas.Children.Add(marker);
        }
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeekSlider || _sourceOpening) return;
        var duration = GetAuthoritativeDuration(Player.MediaPlayer.PlaybackSession);
        if (duration <= TimeSpan.Zero) return;

        var seconds = Math.Clamp(e.NewValue, 0, duration.TotalSeconds);
        // Preserve the Slider value; captured Thumb coordinates are not authoritative.
        _requestedSeekSeconds = seconds;
        var preview = TimeSpan.FromSeconds(seconds);
        CurrentTimeText.Text = Format(preview);
        if (_userSeeking)
        {
            UpdateSeekPreview(preview);
            SeekPreviewPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateSeekPreview(TimeSpan position)
    {
        SeekPreviewText.Text = Format(position);
        var chapter = _viewModel.Chapters
            .Where(x => x.StartPositionTicks <= position.Ticks)
            .OrderByDescending(x => x.StartPositionTicks)
            .FirstOrDefault();

        SeekPreviewChapterText.Text = chapter?.Name ?? string.Empty;
        if (chapter is not null && !string.IsNullOrWhiteSpace(chapter.ImageUrl))
        {
            try { SeekPreviewImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(chapter.ImageUrl)); }
            catch { SeekPreviewImage.Source = null; }
        }
        else
        {
            SeekPreviewImage.Source = null;
        }
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ShowPlayerControls();
        _controlsHideTimer.Stop();
        _userSeeking = true;
        _requestedSeekSeconds = SeekSlider.Value;
        UpdateSeekPreview(TimeSpan.FromSeconds(_requestedSeekSeconds));
        SeekPreviewPanel.Visibility = Visibility.Visible;
    }

    private async void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var duration = GetAuthoritativeDuration(Player.MediaPlayer.PlaybackSession);
        if (duration <= TimeSpan.Zero)
        {
            _userSeeking = false;
            _requestedSeekSeconds = double.NaN;
            SeekPreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var targetSeconds = double.IsNaN(_requestedSeekSeconds)
            ? SeekSlider.Value
            : _requestedSeekSeconds;
        var target = TimeSpan.FromSeconds(Math.Clamp(targetSeconds, 0, duration.TotalSeconds));

        _updatingSeekSlider = true;
        SeekSlider.Value = target.TotalSeconds;
        _updatingSeekSlider = false;
        CurrentTimeText.Text = Format(target);

        _userSeeking = false;
        _requestedSeekSeconds = double.NaN;
        SeekPreviewPanel.Visibility = Visibility.Collapsed;
        await SeekToLogicalPositionAsync(target);
        ShowPlayerControls();
    }

    private async Task SeekToLogicalPositionAsync(TimeSpan target)
    {
        if (_selection is null || _sourceOpening) return;
        if (_syncPlayJoined)
        {
            try { await App.Jellyfin.SyncPlaySeekAsync(target.Ticks); }
            catch { }
        }
        var duration = GetAuthoritativeDuration(Player.MediaPlayer.PlaybackSession);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && target > duration) target = duration;

        if (string.Equals(_viewModel.CurrentPlayMethod, "DirectPlay", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Player.MediaPlayer.PlaybackSession.Position = target;
                CurrentTimeText.Text = Format(target);
                _ = ReportPlaybackProgressAsync();
            }
            catch { }
            return;
        }

        // Seek HLS/transcodes by renegotiating at the absolute StartTimeTicks.
        var targetTicks = Math.Max(0, target.Ticks);

        // Freeze the old source while the replacement HLS stream is negotiated.
        _sourceOpening = true;
        _pendingSeekTicks = targetTicks;
        _viewModel.ResumePositionTicks = targetTicks;
        try { Player.MediaPlayer.Pause(); } catch { }
        _timer.Stop();
        await ReportPlaybackStoppedAsync();

        if (_playbackItem is not null)
            _playbackItem.AudioTracksChanged -= PlaybackItem_AudioTracksChanged;
        Player.Source = null;
        _playbackItem = null;
        ++_sourceGeneration;
        await Task.Yield();

        try
        {
            await _viewModel.NegotiateAsync(
                _selection,
                targetTicks,
                default,
                enableDirectPlay: !_audioFallbackAttempted,
                allowAudioStreamCopy: !_audioFallbackAttempted);
            ErrorPanel.Visibility = Visibility.Collapsed;
            await StartSourceAsync();
        }
        catch (Exception ex)
        {
            _sourceOpening = false;
            _viewModel.ErrorMessage = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void Player_Tapped(object sender, TappedRoutedEventArgs e)
    {
        TogglePlayback();
        ShowPlayerControls();
    }

    private void PlaybackShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        switch (sender.Key)
        {
            case Windows.System.VirtualKey.Escape:
                if (App.MainWindow is MainWindow escWindow && escWindow.IsFullScreen)
                {
                    ShowMouseCursor();
                    escWindow.ExitFullScreen();
                    FullScreenGlyph.Text = "\uE740";
                    ToolTipService.SetToolTip(FullScreenButton, "Fullscreen");
                    args.Handled = true;
                }
                return;

            case Windows.System.VirtualKey.Space:
            case Windows.System.VirtualKey.K:
                TogglePlayback();
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.Left:
                _ = SeekByAsync(-10);
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.Right:
                _ = SeekByAsync(30);
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.F:
                ToggleFullScreenFromKeyboard();
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.M:
                ToggleMute();
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.Up:
                SetVolume(Player.MediaPlayer.Volume + 0.05);
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.Down:
                SetVolume(Player.MediaPlayer.Volume - 0.05);
                ShowPlayerControls();
                args.Handled = true;
                return;

            case Windows.System.VirtualKey.D:
                DiagnosticsPanel.Visibility = DiagnosticsPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                if (DiagnosticsPanel.Visibility == Visibility.Visible)
                    _ = RefreshServerPlaybackStatsAsync();
                ShowPlayerControls();
                args.Handled = true;
                return;
        }
    }

    private void GamepadTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isShuttingDown || !App.Settings.GamepadEnabled) return;
        var pad = Gamepad.Gamepads.FirstOrDefault();
        if (pad is null)
        {
            _lastGamepadButtons = GamepadButtons.None;
            return;
        }

        var buttons = pad.GetCurrentReading().Buttons;
        var pressed = buttons & ~_lastGamepadButtons;
        _lastGamepadButtons = buttons;

        if ((pressed & GamepadButtons.A) != 0) TogglePlayback();
        if ((pressed & GamepadButtons.B) != 0)
        {
            if (App.MainWindow is MainWindow window && window.IsFullScreen) window.ExitFullScreen();
            else if (Frame.CanGoBack) Frame.GoBack();
        }
        if ((pressed & GamepadButtons.LeftShoulder) != 0) _ = SeekByAsync(-10);
        if ((pressed & GamepadButtons.RightShoulder) != 0) _ = SeekByAsync(30);
        if ((pressed & GamepadButtons.X) != 0) ToggleMute();
        if ((pressed & GamepadButtons.Y) != 0) ToggleFullScreenFromKeyboard();

        if (pressed != GamepadButtons.None)
            ShowPlayerControls();
    }

    private void ToggleFullScreenFromKeyboard()
    {
        if (App.MainWindow is not MainWindow window) return;
        var fullScreen = window.ToggleFullScreen();
        FullScreenGlyph.Text = fullScreen ? "\uE73F" : "\uE740";
        ToolTipService.SetToolTip(FullScreenButton, fullScreen ? "Exit fullscreen" : "Fullscreen");
    }

    private void TogglePlayback()
    {
        var player = Player.MediaPlayer;
        var session = player.PlaybackSession;
        if (session.PlaybackState == MediaPlaybackState.Playing)
        {
            player.Pause();
            if (_syncPlayJoined) _ = App.Jellyfin.SyncPlayPauseAsync();
        }
        else
        {
            player.Play();
            if (_syncPlayJoined) _ = App.Jellyfin.SyncPlayUnpauseAsync();
        }

        _ = ReportPlaybackProgressAsync();
        UpdatePlayPause();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
        ShowPlayerControls();
    }

    private async void Back10_Click(object sender, RoutedEventArgs e) => await SeekByAsync(-10);
    private async void Forward30_Click(object sender, RoutedEventArgs e) => await SeekByAsync(30);

    private async Task SeekByAsync(double seconds)
    {
        var current = GetLogicalPosition(Player.MediaPlayer.PlaybackSession);
        await SeekToLogicalPositionAsync(current + TimeSpan.FromSeconds(seconds));
    }

    private void PlaybackInfo_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = DiagnosticsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        ChaptersPanel.Visibility = Visibility.Collapsed;
        SyncPlayPanel.Visibility = Visibility.Collapsed;
        if (DiagnosticsPanel.Visibility == Visibility.Visible)
            _ = RefreshServerPlaybackStatsAsync();
        ShowPlayerControls();
    }

    private void Chapters_Click(object sender, RoutedEventArgs e)
    {
        ChaptersPanel.Visibility = ChaptersPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SyncPlayPanel.Visibility = Visibility.Collapsed;
        AudioPanel.Visibility = Visibility.Collapsed;
        SubtitlePanel.Visibility = Visibility.Collapsed;
        SpeedPanel.Visibility = Visibility.Collapsed;
        ShowPlayerControls();
    }

    private async void ChaptersList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ChapterPreview chapter)
        {
            ChaptersPanel.Visibility = Visibility.Collapsed;
            await SeekToLogicalPositionAsync(TimeSpan.FromTicks(chapter.StartPositionTicks));
            ShowPlayerControls();
        }
    }

    private void NextEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.HasNextEpisode)
            PlayNextEpisode();
    }

    private async void SyncPlay_Click(object sender, RoutedEventArgs e)
    {
        SyncPlayPanel.Visibility = SyncPlayPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        ChaptersPanel.Visibility = Visibility.Collapsed;
        AudioPanel.Visibility = Visibility.Collapsed;
        SubtitlePanel.Visibility = Visibility.Collapsed;
        SpeedPanel.Visibility = Visibility.Collapsed;
        if (SyncPlayPanel.Visibility == Visibility.Visible)
            await RefreshSyncPlayGroupsAsync();
        ShowPlayerControls();
    }

    private async Task RefreshSyncPlayGroupsAsync()
    {
        try
        {
            var groups = await App.Jellyfin.GetSyncPlayGroupsAsync();
            SyncPlayGroupsList.ItemsSource = groups;
            SyncPlayStatus.Text = groups.Count == 0 ? "No active groups." : $"{groups.Count} group(s) available.";
        }
        catch (Exception ex)
        {
            SyncPlayStatus.Text = $"SyncPlay unavailable: {ex.Message}";
        }
    }

    private async void SyncPlayCreate_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(SyncPlayGroupName.Text) ? "Lumen Watch Party" : SyncPlayGroupName.Text.Trim();
        try
        {
            await App.Jellyfin.CreateSyncPlayGroupAsync(name);
            SyncPlayStatus.Text = $"Created “{name}”.";
            await RefreshSyncPlayGroupsAsync();
        }
        catch (Exception ex) { SyncPlayStatus.Text = ex.Message; }
    }

    private async void SyncPlayGroupsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SyncPlayGroupDto { GroupId: { Length: > 0 } id } group)
        {
            try
            {
                await App.Jellyfin.JoinSyncPlayGroupAsync(id);
                _syncPlayJoined = true;
                SyncPlayStatus.Text = $"Joined {group.GroupName ?? "SyncPlay group"} • playback controls are now shared.";
            }
            catch (Exception ex) { SyncPlayStatus.Text = ex.Message; }
        }
    }

    private async void SyncPlayLeave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await App.Jellyfin.LeaveSyncPlayGroupAsync();
            _syncPlayJoined = false;
            SyncPlayStatus.Text = "Left SyncPlay group.";
            await RefreshSyncPlayGroupsAsync();
        }
        catch (Exception ex) { SyncPlayStatus.Text = ex.Message; }
    }

    private async Task RefreshServerPlaybackStatsAsync()
    {
        if (_statsRefreshRunning || _viewModel.IsOfflinePlayback) return;
        _statsRefreshRunning = true;
        try
        {
            var sessions = await App.Jellyfin.GetSessionsAsync();
            var session = sessions.FirstOrDefault(x =>
                string.Equals(x.NowPlayingItem?.Id, _viewModel.ItemId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Client, "Lumen", StringComparison.OrdinalIgnoreCase))
                ?? sessions.FirstOrDefault(x =>
                    string.Equals(x.NowPlayingItem?.Id, _viewModel.ItemId, StringComparison.OrdinalIgnoreCase));

            var info = session?.TranscodingInfo;
            if (info is not null)
            {
                _viewModel.StatsTranscodeSpeed = info.Speed is > 0 ? $"{info.Speed:0.00}×" : string.Empty;
                _viewModel.StatsTranscodeProgress = info.CompletionPercentage is >= 0
                    ? $"{info.CompletionPercentage:0.0}%" + (info.IsThrottled == true ? " • throttled" : string.Empty)
                    : (info.IsThrottled == true ? "Throttled" : string.Empty);
                var outputParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(info.VideoCodec))
                    outputParts.Add(info.VideoCodec.ToUpperInvariant());
                if (!string.IsNullOrWhiteSpace(info.Container))
                    outputParts.Add(info.Container.ToUpperInvariant());
                if (info.Width is > 0 && info.Height is > 0)
                    outputParts.Add($"{info.Width}×{info.Height}");
                if (outputParts.Count > 0)
                    _viewModel.StatsOutput = string.Join(" • ", outputParts);

                _viewModel.StatsHardware = info.IsVideoHWTranscoding == true
                    ? (string.IsNullOrWhiteSpace(info.HardwareAccelerationType)
                        ? "Hardware video transcode"
                        : info.HardwareAccelerationType)
                    : _viewModel.StatsMode == "Transcoding" ? "Software / server selected" : "Not required";
                if (info.TranscodingFramerate is > 0)
                    _viewModel.StatsFrameRate = $"{info.TranscodingFramerate:0.###} fps transcode";
                var bitrate = (info.VideoBitrate ?? 0) + (info.AudioBitrate ?? 0);
                if (bitrate > 0)
                    _viewModel.StatsBitrate = $"{bitrate / 1_000_000d:0.0} Mbps";
            }
        }
        catch { }
        finally
        {
            _lastStatsRefreshUtc = DateTime.UtcNow;
            _statsRefreshRunning = false;
        }
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        ShowPlayerControls();
        AudioPanel.Visibility = Visibility.Collapsed;
        SubtitlePanel.Visibility = Visibility.Collapsed;
        SpeedPanel.Visibility = SpeedPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        RestartControlsHideTimer();
    }

    private void Speed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SpeedComboBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var rate)) return;
        try
        {
            Player.MediaPlayer.PlaybackSession.PlaybackRate = rate;
            SpeedGlyph.Text = rate == 1 ? "1×" : $"{rate:0.##}×";
        }
        catch { }
        ShowPlayerControls();
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        ShowPlayerControls();
        SubtitlePanel.Visibility = Visibility.Collapsed;
        SpeedPanel.Visibility = Visibility.Collapsed;
        AudioPanel.Visibility = AudioPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RestartControlsHideTimer();
    }

    private void Subtitle_Click(object sender, RoutedEventArgs e)
    {
        ShowPlayerControls();
        AudioPanel.Visibility = Visibility.Collapsed;
        SpeedPanel.Visibility = Visibility.Collapsed;
        SubtitlePanel.Visibility = SubtitlePanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RestartControlsHideTimer();
    }


    private void Audio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0 || _selection is null) return;
        _viewModel.SetSelections(_viewModel.SelectedAudioIndex, _viewModel.SelectedSubtitleIndex);
        _audioFallbackAttempted = false;
        ReloadPreservingPosition();
    }

    private void Subtitle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0 || _selection is null) return;
        _viewModel.SetSelections(_viewModel.SelectedAudioIndex, _viewModel.SelectedSubtitleIndex);
        ReloadPreservingPosition();
    }

    private async void ReloadPreservingPosition()
    {
        if (_selection is null) return;
        var session = Player.MediaPlayer.PlaybackSession;
        var currentTicks = Math.Max(0, GetLogicalPosition(session).Ticks);
        _pendingSeekTicks = currentTicks;
        _viewModel.ResumePositionTicks = currentTicks;
        await ReportPlaybackStoppedAsync();
        try
        {
            await _viewModel.NegotiateAsync(_selection, currentTicks);
            ErrorPanel.Visibility = Visibility.Collapsed;
            await StartSourceAsync();
        }
        catch (Exception ex)
        {
            _viewModel.ErrorMessage = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private async Task ReportPlaybackStartAsync()
    {
        if (_viewModel.IsOfflinePlayback) return;
        if (_selection is null || _playbackReported) return;
        _playbackReported = true;
        _stopReported = false;
        _lastProgressReportUtc = DateTime.UtcNow;
        var session = Player.MediaPlayer.PlaybackSession;
        await App.Jellyfin.ReportPlaybackStartAsync(
            _viewModel.ItemId,
            GetLogicalPosition(session).Ticks,
            _viewModel.SelectedAudioIndex,
            _viewModel.SelectedSubtitleIndex,
            _viewModel.ActiveMediaSourceId ?? _selection.MediaSourceId,
            _viewModel.PlaybackSessionId,
            _viewModel.CurrentPlayMethod,
            session.PlaybackState != MediaPlaybackState.Playing);
    }

    private async Task ReportPlaybackProgressAsync()
    {
        if (_viewModel.IsOfflinePlayback) return;
        if (_selection is null || !_playbackReported || _stopReported) return;
        var session = Player.MediaPlayer.PlaybackSession;
        await App.Jellyfin.ReportPlaybackProgressAsync(
            _viewModel.ItemId,
            GetLogicalPosition(session).Ticks,
            _viewModel.SelectedAudioIndex,
            _viewModel.SelectedSubtitleIndex,
            _viewModel.ActiveMediaSourceId ?? _selection.MediaSourceId,
            _viewModel.PlaybackSessionId,
            _viewModel.CurrentPlayMethod,
            session.PlaybackState != MediaPlaybackState.Playing);
    }

    private async Task ReportPlaybackStoppedAsync(bool failed = false)
    {
        if (_viewModel.IsOfflinePlayback) return;
        if (_selection is null || !_playbackReported || _stopReported) return;
        _stopReported = true;
        var session = Player.MediaPlayer.PlaybackSession;
        await App.Jellyfin.ReportPlaybackStoppedAsync(
            _viewModel.ItemId,
            GetLogicalPosition(session).Ticks,
            _viewModel.ActiveMediaSourceId ?? _selection.MediaSourceId,
            _viewModel.PlaybackSessionId);
        _playbackReported = false;
    }

    private void HandlePlaybackStateChanged(MediaPlaybackState state)
    {
        if (_isShuttingDown) return;

        if (state == MediaPlaybackState.Playing)
        {
            RestartControlsHideTimer();
            return;
        }

        _controlsHideTimer.Stop();
        ShowMouseCursor();

        if (!_controlsVisible)
        {
            TopChrome.Visibility = Visibility.Visible;
            ControlsOverlay.Visibility = Visibility.Visible;
            _controlsVisible = true;
        }
    }

    private void UpdatePlayPause()
    {
        var playing = Player.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseGlyph.Text = playing ? "\uE769" : "\uE768";
    }


    private TimeSpan GetLogicalPosition(MediaPlaybackSession session)
    {
        var rawTicks = Math.Max(0, session.Position.Ticks);
        var ticks = _streamBasePositionTicks > 0
            ? _streamBasePositionTicks + rawTicks
            : rawTicks;

        if (_viewModel.RunTimeTicks > 0)
            ticks = Math.Min(ticks, _viewModel.RunTimeTicks);
        return TimeSpan.FromTicks(Math.Max(0, ticks));
    }

    private TimeSpan GetAuthoritativeDuration(MediaPlaybackSession session)
    {
        // Prefer Jellyfin runtime over rolling adaptive-stream duration.
        if (_viewModel.RunTimeTicks > 0)
            return TimeSpan.FromTicks(_viewModel.RunTimeTicks);

        return session.NaturalDuration > TimeSpan.Zero
            ? session.NaturalDuration
            : TimeSpan.Zero;
    }

    private static string Format(TimeSpan time)
    {
        if (time.TotalHours >= 1) return time.ToString(@"h\:mm\:ss");
        return time.ToString(@"mm\:ss");
    }

    private void UpdateSegmentUi()
    {
        if (_mediaSegments.Count == 0 || _sourceOpening || _isShuttingDown)
        {
            SkipSegmentButton.Visibility = Visibility.Collapsed;
            _activeSegment = null;
            return;
        }

        var ticks = GetLogicalPosition(Player.MediaPlayer.PlaybackSession).Ticks;
        var segment = _mediaSegments.FirstOrDefault(x => ticks >= x.StartTicks && ticks < x.EndTicks);
        if (segment is null)
        {
            SkipSegmentButton.Visibility = Visibility.Collapsed;
            CreditsNextOverlay.Visibility = Visibility.Collapsed;
            _activeSegment = null;
            return;
        }

        var key = $"{segment.Type}:{segment.StartTicks}:{segment.EndTicks}";
        _activeSegment = segment;

        var label = segment.Type?.ToLowerInvariant() switch
        {
            "intro" => "Skip Intro",
            "recap" => "Skip Recap",
            "outro" => "Skip Credits",
            _ => "Skip Segment"
        };
        SkipSegmentText.Text = label;
        SkipSegmentButton.Visibility = Visibility.Visible;

        if (string.Equals(segment.Type, "Outro", StringComparison.OrdinalIgnoreCase) &&
            _viewModel.HasNextEpisode && _handledSegments.Add($"credits-card:{key}"))
        {
            CreditsNextOverlay.Visibility = Visibility.Visible;
            _controlsHideTimer.Stop();
            ShowMouseCursor();
        }

        if (CreditsNextOverlay.Visibility == Visibility.Visible)
            SkipSegmentButton.Visibility = Visibility.Collapsed;

        var shouldAutoSkip =
            string.Equals(segment.Type, "Intro", StringComparison.OrdinalIgnoreCase) && App.Settings.AutoSkipIntro ||
            string.Equals(segment.Type, "Outro", StringComparison.OrdinalIgnoreCase) && App.Settings.AutoSkipCredits;

        if (shouldAutoSkip && _handledSegments.Add(key))
            _ = SeekToLogicalPositionAsync(TimeSpan.FromTicks(segment.EndTicks));
    }

    private async void SkipSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSegment is null) return;
        var key = $"{_activeSegment.Type}:{_activeSegment.StartTicks}:{_activeSegment.EndTicks}";
        _handledSegments.Add(key);
        await SeekToLogicalPositionAsync(TimeSpan.FromTicks(_activeSegment.EndTicks));
        SkipSegmentButton.Visibility = Visibility.Collapsed;
        ShowPlayerControls();
    }

    private void CreditsPlayNext_Click(object sender, RoutedEventArgs e)
    {
        CreditsNextOverlay.Visibility = Visibility.Collapsed;
        PlayNextEpisode();
    }

    private void CreditsKeepWatching_Click(object sender, RoutedEventArgs e)
    {
        CreditsNextOverlay.Visibility = Visibility.Collapsed;
        ShowPlayerControls();
    }

    private void CreditsClosePlayer_Click(object sender, RoutedEventArgs e)
    {
        CreditsNextOverlay.Visibility = Visibility.Collapsed;
        Close_Click(sender, e);
    }

    private void BeginUpNextCountdown()
    {
        PlayerCinematicTransform.ScaleX = 0.74;
        PlayerCinematicTransform.ScaleY = 0.74;
        PlayerCinematicTransform.TranslateX = -170;
        if (!_viewModel.HasNextEpisode)
            return;

        UpNextPanel.Visibility = Visibility.Visible;
        ShowPlayerControls();
        _upNextTimer.Stop();

        if (!App.Settings.AutoplayNextEpisode)
        {
            UpNextCountdownText.Text = "Autoplay is off";
            return;
        }

        _upNextSeconds = 10;
        UpNextCountdownText.Text = $"Starting in {_upNextSeconds} seconds";
        _upNextTimer.Start();
    }

    private void UpNextTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_isShuttingDown) return;
        _upNextSeconds--;
        if (_upNextSeconds <= 0)
        {
            _upNextTimer.Stop();
            PlayNextEpisode();
            return;
        }

        UpNextCountdownText.Text = $"Starting in {_upNextSeconds} second{(_upNextSeconds == 1 ? string.Empty : "s")}";
    }

    private void PlayNext_Click(object sender, RoutedEventArgs e)
    {
        _upNextTimer.Stop();
        PlayNextEpisode();
    }

    private void CancelUpNext_Click(object sender, RoutedEventArgs e)
    {
        PlayerCinematicTransform.ScaleX = 1;
        PlayerCinematicTransform.ScaleY = 1;
        PlayerCinematicTransform.TranslateX = 0;
        _upNextTimer.Stop();
        UpNextPanel.Visibility = Visibility.Collapsed;
        ShowPlayerControls();
    }

    private void PlayNextEpisode()
    {
        PlayerCinematicTransform.ScaleX = 1;
        PlayerCinematicTransform.ScaleY = 1;
        PlayerCinematicTransform.TranslateX = 0;
        if (string.IsNullOrWhiteSpace(_viewModel.NextEpisodeId)) return;
        _upNextTimer.Stop();
        UpNextPanel.Visibility = Visibility.Collapsed;

        var next = new PlaybackSelection
        {
            ItemId = _viewModel.NextEpisodeId,
            ResumePositionTicks = 0,
            StartFromBeginning = true
        };

        if (App.Settings.PreferDownloadedPlayback)
        {
            var local = App.Downloads.GetCompletedDownload(_viewModel.NextEpisodeId);
            if (local is not null)
            {
                next.LocalFilePath = local.LocalPath;
                next.IsOffline = true;
            }
        }

        Frame.Navigate(typeof(PlayerPage), next);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
        ShowPlayerControls();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingVolumeUi || Player?.MediaPlayer is null) return;

        Player.MediaPlayer.Volume = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        if (Player.MediaPlayer.Volume > 0 && Player.MediaPlayer.IsMuted)
            Player.MediaPlayer.IsMuted = false;

        UpdateVolumeGlyph();
        ShowPlayerControls();
    }

    private void ToggleMute()
    {
        Player.MediaPlayer.IsMuted = !Player.MediaPlayer.IsMuted;
        UpdateVolumeGlyph();
    }

    private void SetVolume(double volume)
    {
        var value = Math.Clamp(volume, 0.0, 1.0);
        Player.MediaPlayer.Volume = value;
        if (value > 0)
            Player.MediaPlayer.IsMuted = false;

        _updatingVolumeUi = true;
        VolumeSlider.Value = value * 100.0;
        _updatingVolumeUi = false;
        UpdateVolumeGlyph();
    }

    private void UpdateVolumeGlyph()
    {
        if (VolumeGlyph is null || Player?.MediaPlayer is null) return;

        if (Player.MediaPlayer.IsMuted || Player.MediaPlayer.Volume <= 0.001)
            VolumeGlyph.Text = "\uE74F"; // Mute
        else if (Player.MediaPlayer.Volume < 0.5)
            VolumeGlyph.Text = "\uE993"; // Volume 1
        else
            VolumeGlyph.Text = "\uE767"; // Volume
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
        {
            var fullScreen = window.ToggleFullScreen();
            FullScreenGlyph.Text = fullScreen ? "\uE73F" : "\uE740";
            ToolTipService.SetToolTip(FullScreenButton, fullScreen ? "Exit fullscreen" : "Fullscreen");
            ShowPlayerControls();
        }
    }

    // Kept for stale generated XAML.
    private void Compatibility_Click(object sender, RoutedEventArgs e)
    {
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        ShowMouseCursor();
        var finalPositionTicks = GetLogicalPosition(Player.MediaPlayer.PlaybackSession).Ticks;
        await ReportPlaybackStoppedAsync();
        HomePage.NotifyPlaybackPositionChanged(
            _viewModel.ItemId,
            finalPositionTicks,
            _viewModel.RunTimeTicks);
        ShutdownPlayer();
        if (App.MainWindow is MainWindow closeWindow) closeWindow.ExitFullScreen();
        if (_playbackItem is not null)
            _playbackItem.AudioTracksChanged -= PlaybackItem_AudioTracksChanged;
        _playbackItem = null;
        try { Player.Source = null; } catch { }
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        ShowMouseCursor();
        var finalPositionTicks = GetLogicalPosition(Player.MediaPlayer.PlaybackSession).Ticks;
        await ReportPlaybackStoppedAsync();
        HomePage.NotifyPlaybackPositionChanged(
            _viewModel.ItemId,
            finalPositionTicks,
            _viewModel.RunTimeTicks);
        ShutdownPlayer();
        if (App.MainWindow is MainWindow backWindow) backWindow.ExitFullScreen();
        if (_playbackItem is not null)
            _playbackItem.AudioTracksChanged -= PlaybackItem_AudioTracksChanged;
        _playbackItem = null;
        try { Player.Source = null; } catch { }
        if (Frame.CanGoBack) Frame.GoBack();
    }

    protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try { ShowMouseCursor(); } catch { }

        if (!_isShuttingDown)
        {
            try
            {
                var finalPositionTicks = GetLogicalPosition(Player.MediaPlayer.PlaybackSession).Ticks;
                await ReportPlaybackStoppedAsync();
                HomePage.NotifyPlaybackPositionChanged(
                    _viewModel.ItemId,
                    finalPositionTicks,
                    _viewModel.RunTimeTicks);
            }
            catch { }
        }

        ShutdownPlayer();

        if (App.MainWindow is MainWindow navWindow)
        {
            try { navWindow.ExitFullScreen(); } catch { }
        }

        base.OnNavigatedFrom(e);
    }

    public void PrepareForAppShutdown()
    {
        // Tear down MediaPlayerElement while its WinRT objects are still valid.
        try { _gamepadTimer?.Stop(); } catch { }
        try { _timer.Stop(); } catch { }
        try { _controlsHideTimer.Stop(); } catch { }
        try { _upNextTimer.Stop(); } catch { }

        try
        {
            var mediaPlayer = Player?.MediaPlayer;
            if (mediaPlayer is not null)
            {
                try { mediaPlayer.CommandManager.IsEnabled = false; } catch { }
                try { mediaPlayer.MediaOpened -= MediaOpened; } catch { }
                try { mediaPlayer.MediaFailed -= MediaFailed; } catch { }
                try { mediaPlayer.MediaEnded -= MediaEnded; } catch { }
                try { mediaPlayer.Pause(); } catch { }
                try { mediaPlayer.Source = null; } catch { }
            }
        }
        catch { }

        try { if (Player is not null) Player.Source = null; } catch { }
        _sourceOpening = true;
        _isShuttingDown = true;
        _playbackItem = null;
    }

    public void ShutdownPlayer()
    {
        PlayerCinematicTransform.ScaleX = 1;
        PlayerCinematicTransform.ScaleY = 1;
        PlayerCinematicTransform.TranslateX = 0;
        _gamepadTimer?.Stop();
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        _sourceOpening = true;
        _sourceGeneration++;

        try { _timer.Stop(); } catch { }
        try { _controlsHideTimer.Stop(); } catch { }
        try { _upNextTimer.Stop(); } catch { }

        try
        {
            PlayerRoot.InputCursor = null;
            _transparentCursor.Dispose();
            _arrowCursor.Dispose();
        }
        catch { }

        try
        {
            if (_playbackItem is not null)
            {
                _playbackItem.AudioTracksChanged -= PlaybackItem_AudioTracksChanged;
                _playbackItem.TimedMetadataTracksChanged -= PlaybackItem_TimedMetadataTracksChanged;
            }
        }
        catch { }

        try
        {
            var mediaPlayer = Player?.MediaPlayer;
            if (mediaPlayer is not null)
            {
                try { mediaPlayer.MediaOpened -= MediaOpened; } catch { }
                try { mediaPlayer.MediaFailed -= MediaFailed; } catch { }
                try { mediaPlayer.MediaEnded -= MediaEnded; } catch { }
                try { mediaPlayer.Pause(); } catch { }
                try { mediaPlayer.Source = null; } catch { }
            }
        }
        catch { }

        try
        {
            if (Player is not null)
                Player.Source = null;
        }
        catch { }

        _playbackItem = null;
    }

    private async void MediaEnded(MediaPlayer sender, object args)
    {
        // Never let an async-void MediaEnded exception terminate the app.
        try
        {
            try { sender.Pause(); } catch { }
            try { await ReportPlaybackStoppedAsync(); } catch { }
            try { await App.Jellyfin.MarkPlayedAsync(_viewModel.ItemId, true); } catch { }

            // Refresh Next Up after Jellyfin records completion.
            try { await _viewModel.RefreshNextEpisodeAsync(); } catch { }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isShuttingDown)
                    return;

                if (_viewModel.HasNextEpisode)
                {
                    BeginUpNextCountdown();
                    return;
                }

                ShowMouseCursor();
                if (App.MainWindow is MainWindow window)
                    window.ExitFullScreen();

                if (!string.IsNullOrWhiteSpace(_viewModel.ItemId))
                    Frame.Navigate(typeof(ItemPage), _viewModel.ItemId);
                else if (Frame.CanGoBack)
                    Frame.GoBack();
            });
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try { ShowMouseCursor(); } catch { }
                try { ShowPlayerControls(); } catch { }
            });
        }
    }
}

