using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Lumen.ViewModels;
using Lumen.Views;
using Lumen.Services;
using Lumen.Models;
using System.Runtime.InteropServices;
using Windows.Gaming.Input;

namespace Lumen;

public sealed class AccountPickerItem
{
    public string UserId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public bool IsRemembered { get; init; }
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[0].ToString().ToUpperInvariant();
    public string Status => IsCurrent ? "Current profile" : IsRemembered ? "Remembered" : string.Empty;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
}

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<CommandPaletteResult> _commandPaletteItems = [];
    private readonly ObservableCollection<AccountPickerItem> _accountPickerItems = [];
    private CancellationTokenSource? _commandPaletteCts;
    private CancellationTokenSource? _topSearchCts;
    private string _pendingLaunchArgument = string.Empty;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _reconnectTimer;
    private bool _reconnectCheckRunning;
    private bool _isShuttingDown;
    private bool _hiddenToTray;
    private IntPtr _hwnd;
    private const uint WM_TRAYICON = 0x8000 + 42;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_COMMAND = 0x0111;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int ID_TRAY_OPEN = 41001;
    private const int ID_TRAY_DOWNLOADS = 41002;
    private const int ID_TRAY_EXIT = 41003;
    private bool _trayIconAdded;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _gamepadInputTimer;
    private GamepadButtons _lastGamepadButtons;
    private nuint _lastStickDirection;
    private int _lastNavigationDirection;
    private DateTime _lastNavigationMoveUtc = DateTime.MinValue;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _miniPlayerTimer;
    private bool _updatingMiniProgress;
    private bool _musicShellMode;
    private LibraryViewModel? _currentMusicLibrary;
    private ITaskbarList3? _taskbarList;
    private bool _offlineShellMode;

    public MainWindow()
    {
        InitializeComponent();
        Root.DataContext = App.MainViewModel;
        _hwnd = WindowNative.GetWindowHandle(this);
        try
        {
            _taskbarList = (ITaskbarList3)new CTaskbarList();
            _taskbarList.HrInit();
        }
        catch { _taskbarList = null; }
        SetWindowIcon();
        MaximizeOnStartup();
        InstallWindowHook();
        LoginView.LoginSucceeded += LoginView_LoginSucceeded;
        LoginView.LoginCancelled += LoginView_LoginCancelled;
        Closed += MainWindow_Closed;
        CommandPaletteResults.ItemsSource = _commandPaletteItems;
        AccountPickerList.ItemsSource = _accountPickerItems;
        QuickDownloadsList.ItemsSource = App.Downloads.Items;
        App.Downloads.Items.CollectionChanged += Downloads_CollectionChanged;
        ContentFrame.Navigated += ContentFrame_Navigated;
        App.MusicPlayback.Changed += MusicPlayback_Changed;
        _miniPlayerTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _miniPlayerTimer.Interval = TimeSpan.FromMilliseconds(500);
        _miniPlayerTimer.IsRepeating = true;
        _miniPlayerTimer.Tick += MiniPlayerTimer_Tick;
        _miniPlayerTimer.Start();

        _reconnectTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _reconnectTimer.Interval = TimeSpan.FromSeconds(15);
        _reconnectTimer.IsRepeating = true;
        _reconnectTimer.Tick += ReconnectTimer_Tick;
        _gamepadInputTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _gamepadInputTimer.Interval = TimeSpan.FromMilliseconds(50);
        _gamepadInputTimer.IsRepeating = true;
        _gamepadInputTimer.Tick += GamepadInputTimer_Tick;
        _gamepadInputTimer.Start();
    }



    private void MusicPlayback_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateMiniPlayer);

    private void UpdateMiniPlayer()
    {
        var music = App.MusicPlayback;
        var fullPlayerOpen = ContentFrame.Content is MusicPlayerPage;
        MiniPlayer.Visibility = music.HasTrack && !fullPlayerOpen && ShellRoot.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        if (!music.HasTrack) return;
        MiniTitle.Text = music.CurrentItem?.Name ?? "Unknown track";
        MiniArtist.Text = music.CurrentItem?.Artists?.FirstOrDefault() ?? music.CurrentItem?.AlbumArtist ?? "Unknown artist";
        MiniAlbum.Text = music.CurrentItem?.Album ?? string.Empty;
        MiniPlayPauseIcon.Glyph = music.IsPlaying ? "\uE769" : "\uE768";
        MiniArtwork.Source = Uri.TryCreate(music.ArtworkUrl, UriKind.Absolute, out var uri)
            ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) : null;
        MiniShuffleButton.Opacity = music.ShuffleEnabled ? 1 : .55;
        MiniRepeatButton.Opacity = music.RepeatMode == MusicRepeatMode.Off ? .55 : 1;
        MiniRepeatIcon.Glyph = music.RepeatMode == MusicRepeatMode.One ? "\uE8ED" : "\uE8EE";
        ToolTipService.SetToolTip(MiniRepeatButton, music.RepeatMode switch
        {
            MusicRepeatMode.One => "Repeat one",
            MusicRepeatMode.All => "Repeat all",
            _ => "Repeat off"
        });
        MiniQueueList.ItemsSource = music.Queue.ToList();
        MiniQueueCount.Text = $"{music.Queue.Count} song{(music.Queue.Count == 1 ? "" : "s")}";
    }

    private void MiniPlayerTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!App.MusicPlayback.HasTrack) { UpdateMiniPlayer(); return; }
        var session = App.MusicPlayback.Player.PlaybackSession;
        _updatingMiniProgress = true;
        MiniProgress.Maximum = Math.Max(1, session.NaturalDuration.TotalSeconds);
        MiniProgress.Value = Math.Clamp(session.Position.TotalSeconds, 0, MiniProgress.Maximum);
        MiniTime.Text = $"{FormatMiniTime(session.Position)} / {FormatMiniTime(session.NaturalDuration)}";
        _updatingMiniProgress = false;
        MiniPlayPauseIcon.Glyph = App.MusicPlayback.IsPlaying ? "\uE769" : "\uE768";
    }
    private static string FormatMiniTime(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    private void MiniPlayPause_Click(object sender, RoutedEventArgs e) => App.MusicPlayback.PlayPause();
    private async void MiniPrevious_Click(object sender, RoutedEventArgs e) => await App.MusicPlayback.PreviousAsync();
    private async void MiniNext_Click(object sender, RoutedEventArgs e) => await App.MusicPlayback.NextAsync();
    private void MiniShuffle_Click(object sender, RoutedEventArgs e) => App.MusicPlayback.ToggleShuffle();
    private void MiniRepeat_Click(object sender, RoutedEventArgs e) => App.MusicPlayback.CycleRepeat();
    private void MiniClose_Click(object sender, RoutedEventArgs e) => App.MusicPlayback.StopAndClear();
    private void MiniLyrics_Click(object sender, RoutedEventArgs e)
    {
        if (App.MusicPlayback.CurrentItem?.Id is string id)
            ContentFrame.Navigate(typeof(MusicPlayerPage), id);
    }
    private void MiniVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        App.MusicPlayback.Player.Volume = Math.Clamp(e.NewValue / 100d, 0, 1);
    }
    private async void MiniQueue_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not BaseItemDto item || string.IsNullOrWhiteSpace(item.Id)) return;
        var index = App.MusicPlayback.Queue.FindIndex(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) await App.MusicPlayback.PlayQueueIndexAsync(index);
    }
    private void MiniProgress_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingMiniProgress) App.MusicPlayback.Seek(TimeSpan.FromSeconds(e.NewValue));
    }
    private void MiniPlayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (DependencyObject? n = source; n is not null; n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(n))
                if (n is Button or Slider) return;
        }
        if (App.MusicPlayback.CurrentItem?.Id is string id) ContentFrame.Navigate(typeof(MusicPlayerPage), id);
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DownloadEntry item in e.OldItems)
                item.PropertyChanged -= DownloadEntry_PropertyChanged;

        if (e.NewItems is not null)
            foreach (DownloadEntry item in e.NewItems)
                item.PropertyChanged += DownloadEntry_PropertyChanged;

        RefreshQuickDownloadButton();
    }

    private void DownloadEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadEntry.Status)
            or nameof(DownloadEntry.IsComplete)
            or nameof(DownloadEntry.Progress)
            or nameof(DownloadEntry.ShowInQuickHistory))
        {
            if (DispatcherQueue.HasThreadAccess)
                RefreshQuickDownloadButton();
            else
                DispatcherQueue.TryEnqueue(RefreshQuickDownloadButton);
        }
    }

    private void RefreshQuickDownloadButton()
    {
        var active = App.Downloads.Items.Where(IsQuickDownloadActive).ToList();
        var history = App.Downloads.Items
            .Where(x => x.ShowInQuickHistory || IsQuickDownloadActive(x))
            .ToList();

        var activeCount = active.Count;
        var hasActive = activeCount > 0;
        var hasHistory = history.Count > 0;

        // Keep completed downloads until dismissed.
        QuickDownloadsList.ItemsSource = history;

        DownloadQuickButton.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        DownloadQuickBadge.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        DownloadsBadge.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        QuickDownloadsClearButton.IsEnabled = history.Any(x => !IsQuickDownloadActive(x));

        var countText = activeCount > 99 ? "99+" : activeCount.ToString();
        DownloadQuickBadgeText.Text = countText;
        DownloadsBadgeText.Text = countText;

        UpdateTaskbarDownloadProgress(active);
    }

    private static bool IsQuickDownloadActive(DownloadEntry entry)
        => entry.Status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Queued", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Paused", StringComparison.OrdinalIgnoreCase);

    private void UpdateTaskbarDownloadProgress(IReadOnlyList<DownloadEntry> active)
    {
        if (_taskbarList is null || _hwnd == IntPtr.Zero)
            return;

        try
        {
            if (active.Count == 0)
            {
                _taskbarList.SetProgressState(_hwnd, TaskbarProgressState.NoProgress);
                return;
            }

            var allPaused = active.All(x =>
                string.Equals(x.Status, "Paused", StringComparison.OrdinalIgnoreCase));
            _taskbarList.SetProgressState(
                _hwnd,
                allPaused ? TaskbarProgressState.Paused : TaskbarProgressState.Normal);

            const ulong scale = 1000;
            var completed = (ulong)Math.Clamp(
                active.Sum(x => Math.Clamp(x.Progress, 0, 100)) / active.Count * 10d,
                0,
                scale);
            _taskbarList.SetProgressValue(_hwnd, completed, scale);
        }
        catch { }
    }

    private void QuickPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry })
            App.Downloads.Pause(entry);
    }

    private async void QuickResume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry } button)
            return;

        button.IsEnabled = false;
        try { await App.Downloads.ResumeAsync(entry); }
        catch { }
        finally { button.IsEnabled = true; }
    }

    private void QuickCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry })
            App.Downloads.Cancel(entry);
    }

    private void QuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry } && entry.IsComplete && File.Exists(entry.LocalPath))
        {
            DownloadQuickButton.Flyout?.Hide();
            ContentFrame.Navigate(typeof(PlayerPage), new PlaybackSelection
            {
                ItemId = entry.ItemId,
                LocalFilePath = entry.LocalPath,
                IsOffline = true
            });
        }
    }

    private async void QuickRetry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry } button || IsQuickDownloadActive(entry))
            return;

        button.IsEnabled = false;
        try
        {
            var item = await App.Jellyfin.GetItemAsync(entry.ItemId);
            if (item is not null)
                await App.Downloads.DownloadAsync(item, entry.QualityId);
        }
        catch { }
        finally { button.IsEnabled = true; }
    }

    private async void QuickDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry })
            await App.Downloads.DismissFromQuickHistoryAsync(entry);
    }

    private async void QuickDownloadsClear_Click(object sender, RoutedEventArgs e)
    {
        await App.Downloads.ClearQuickHistoryAsync();
        RefreshQuickDownloadButton();
    }

    private void QuickDownloadsViewAll_Click(object sender, RoutedEventArgs e)
    {
        DownloadQuickButton.Flyout?.Hide();
        ContentFrame.Navigate(typeof(DownloadsPage));
        App.MainViewModel.PageTitle = "Downloads";
    }

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState state);
    }

    private const int GWLP_WNDPROC = -4;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const nuint SC_MINIMIZE = 0xF020;
    private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, nuint wParam, nint lParam);
    private WindowProc? _windowProc;
    private IntPtr _oldWindowProc;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newProc);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr newProc);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private static IntPtr SetWindowProc(IntPtr hwnd, IntPtr proc)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, GWLP_WNDPROC, proc) : SetWindowLong32(hwnd, GWLP_WNDPROC, proc);

    private void InstallWindowHook()
    {
        try
        {
            _windowProc = WindowMessage;
            _oldWindowProc = SetWindowProc(_hwnd, Marshal.GetFunctionPointerForDelegate(_windowProc));
        }
        catch { }
    }

    private void GamepadInputTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_isShuttingDown || _hiddenToTray || !App.Settings.GamepadEnabled)
            return;

        try
        {
            if (ContentFrame.Content is PlayerPage)
                return;

            var pad = Gamepad.Gamepads.FirstOrDefault();
            if (pad is null)
            {
                _lastGamepadButtons = GamepadButtons.None;
                _lastStickDirection = 0;
                return;
            }

            var reading = pad.GetCurrentReading();
            var current = reading.Buttons;
            var pressed = current & ~_lastGamepadButtons;
            _lastGamepadButtons = current;

            const double stickThreshold = 0.65;
            var stickDirection = reading.LeftThumbstickY >= stickThreshold ? 1
                : reading.LeftThumbstickY <= -stickThreshold ? 2
                : reading.LeftThumbstickX <= -stickThreshold ? 3
                : reading.LeftThumbstickX >= stickThreshold ? 4
                : 0;

            var digitalDirection = (current & GamepadButtons.DPadUp) != 0 ? 1
                : (current & GamepadButtons.DPadDown) != 0 ? 2
                : (current & GamepadButtons.DPadLeft) != 0 ? 3
                : (current & GamepadButtons.DPadRight) != 0 ? 4
                : 0;
            var navigationDirection = digitalDirection != 0 ? digitalDirection : stickDirection;
            var now = DateTime.UtcNow;

            if (navigationDirection != 0)
            {
                var changed = navigationDirection != _lastNavigationDirection;
                if (changed || now - _lastNavigationMoveUtc >= TimeSpan.FromMilliseconds(155))
                {
                    MoveGamepadFocus(navigationDirection switch
                    {
                        1 => FocusNavigationDirection.Up,
                        2 => FocusNavigationDirection.Down,
                        3 => FocusNavigationDirection.Left,
                        _ => FocusNavigationDirection.Right
                    });
                    _lastNavigationMoveUtc = now;
                }
            }

            if ((pressed & GamepadButtons.A) != 0)
                ActivateGamepadFocusedElement();
            else if ((pressed & GamepadButtons.B) != 0)
            {
                if (ContentFrame.CanGoBack)
                    ContentFrame.GoBack();
            }

            _lastNavigationDirection = navigationDirection;
            _lastStickDirection = (nuint)stickDirection;
        }
        catch
        {
        }
    }

    private void MoveGamepadFocus(FocusNavigationDirection direction)
    {
        // Include shell navigation in controller focus traversal.
        var candidates = GetGamepadFocusableControls(ShellRoot)
            .Select(control => (Control: control, Bounds: GetBounds(control)))
            .Where(x => x.Bounds.Width > 1 && x.Bounds.Height > 1)
            .ToList();

        if (candidates.Count == 0)
            return;

        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        var current = candidates.FirstOrDefault(x => ReferenceEquals(x.Control, focused));

        if (current.Control is null && focused is not null)
        {
            DependencyObject? node = focused;
            while (node is not null)
            {
                var match = candidates.FirstOrDefault(x => ReferenceEquals(x.Control, node));
                if (match.Control is not null)
                {
                    current = match;
                    break;
                }
                node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
            }
        }

        if (current.Control is null)
        {
            var first = candidates
                .OrderBy(x => x.Bounds.Y)
                .ThenBy(x => x.Bounds.X)
                .First();
            first.Control.Focus(FocusState.Keyboard);
            return;
        }

        var sourceX = current.Bounds.X + current.Bounds.Width / 2;
        var sourceY = current.Bounds.Y + current.Bounds.Height / 2;

        (Control Control, Windows.Foundation.Rect Bounds)? PickBest(bool requireAxisOverlap)
        {
            (Control Control, Windows.Foundation.Rect Bounds)? best = null;
            var bestScore = double.MaxValue;
            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate.Control, current.Control)) continue;
                var x = candidate.Bounds.X + candidate.Bounds.Width / 2;
                var y = candidate.Bounds.Y + candidate.Bounds.Height / 2;
                var dx = x - sourceX;
                var dy = y - sourceY;
                var horizontalOverlap = Math.Min(current.Bounds.Right, candidate.Bounds.Right) -
                                        Math.Max(current.Bounds.Left, candidate.Bounds.Left);
                var verticalOverlap = Math.Min(current.Bounds.Bottom, candidate.Bounds.Bottom) -
                                      Math.Max(current.Bounds.Top, candidate.Bounds.Top);

                double primary, secondary;
                bool overlaps;
                switch (direction)
                {
                    case FocusNavigationDirection.Up:
                        if (dy >= -4) continue;
                        primary = -dy; secondary = Math.Abs(dx); overlaps = horizontalOverlap > 8; break;
                    case FocusNavigationDirection.Down:
                        if (dy <= 4) continue;
                        primary = dy; secondary = Math.Abs(dx); overlaps = horizontalOverlap > 8; break;
                    case FocusNavigationDirection.Left:
                        if (dx >= -4) continue;
                        primary = -dx; secondary = Math.Abs(dy); overlaps = verticalOverlap > 8; break;
                    default:
                        if (dx <= 4) continue;
                        primary = dx; secondary = Math.Abs(dy); overlaps = verticalOverlap > 8; break;
                }
                if (requireAxisOverlap && !overlaps) continue;
                var score = primary + secondary * (overlaps ? 2.4 : 5.0);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        var best = PickBest(true) ?? PickBest(false);
        best?.Control.Focus(FocusState.Keyboard);
    }

    private List<Control> GetGamepadFocusableControls(DependencyObject root)
    {
        var result = new List<Control>();
        CollectFocusable(root, result);
        return result;
    }

    private static void CollectFocusable(DependencyObject root, List<Control> result)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Control control &&
                control.Visibility == Visibility.Visible &&
                control.IsEnabled &&
                control.IsTabStop &&
                control.ActualWidth > 1 &&
                control.ActualHeight > 1)
            {
                result.Add(control);
            }

            CollectFocusable(child, result);
        }
    }

    private Windows.Foundation.Rect GetBounds(FrameworkElement element)
    {
        try
        {
            var transform = element.TransformToVisual(ShellRoot);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            return new Windows.Foundation.Rect(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
        catch
        {
            return new Windows.Foundation.Rect();
        }
    }

    private void ActivateGamepadFocusedElement()
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);

        DependencyObject? node = focused as DependencyObject;
        while (node is not null)
        {
            if (node is FrameworkElement element)
            {
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(element);
                if (peer is null && element is Button button)
                    peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);

                if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                    is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
                {
                    invoke.Invoke();
                    return;
                }

                if (node is ListViewItem listItem)
                {
                    listItem.IsSelected = true;
                    return;
                }
                if (node is GridViewItem gridItem)
                {
                    gridItem.IsSelected = true;
                    return;
                }
            }

            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
    }

    private void SeedGamepadFocus()
    {
        if (!App.Settings.GamepadEnabled || _isShuttingDown || ContentFrame.Content is PlayerPage)
            return;

        try
        {
            var candidates = GetGamepadFocusableControls(ContentFrame);
            if (candidates.Count > 0)
                candidates.OrderBy(x => GetBounds(x).Y).ThenBy(x => GetBounds(x).X).First()
                    .Focus(FocusState.Keyboard);
        }
        catch { }
    }

    private NOTIFYICONDATA CreateTrayData(IntPtr icon = default)
        => new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_TIP | (icon != IntPtr.Zero ? NIF_ICON : 0),
            uCallbackMessage = WM_TRAYICON,
            hIcon = icon,
            szTip = "Lumen"
        };

    private void EnsureTrayIcon()
    {
        if (_trayIconAdded || _hwnd == IntPtr.Zero) return;
        IntPtr icon = IntPtr.Zero;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Lumen.ico");
            if (File.Exists(path))
                icon = LoadImage(IntPtr.Zero, path, 1, 16, 16, 0x0010);
            var data = CreateTrayData(icon);
            _trayIconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        }
        catch { }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); }
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded) return;
        try
        {
            var data = CreateTrayData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        catch { }
        _trayIconAdded = false;
    }

    private void RestoreFromTray()
    {
        _hiddenToTray = false;
        ShowWindow(_hwnd, 9); // SW_RESTORE
        SetForegroundWindow(_hwnd);
        Activate();
    }

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, ID_TRAY_OPEN, "Open Lumen");
            AppendMenu(menu, 0, ID_TRAY_DOWNLOADS, "Downloads");
            AppendMenu(menu, 0x00000800, 0, string.Empty);
            AppendMenu(menu, 0, ID_TRAY_EXIT, "Exit");
            SetForegroundWindow(_hwnd);
            if (GetCursorPos(out var pt))
            {
                var command = TrackPopupMenu(menu, TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
                HandleTrayCommand(command);
            }
        }
        finally { DestroyMenu(menu); }
    }

    private void HandleTrayCommand(int command)
    {
        switch (command)
        {
            case ID_TRAY_OPEN:
                RestoreFromTray();
                break;
            case ID_TRAY_DOWNLOADS:
                RestoreFromTray();
                ContentFrame.Navigate(typeof(DownloadsPage));
                App.MainViewModel.PageTitle = "Downloads";
                break;
            case ID_TRAY_EXIT:
                _isShuttingDown = true;
                _gamepadInputTimer.Stop();
                try
                {
                    if (ContentFrame.Content is PlayerPage playerPage)
                        playerPage.PrepareForAppShutdown();
                    else if (ContentFrame.Content is MusicPlayerPage musicPlayerPage)
                        musicPlayerPage.PrepareForAppShutdown();
                    App.MusicPlayback.Shutdown();
                }
                catch { }
                RemoveTrayIcon();
                Close();
                break;
        }
    }

    private IntPtr WindowMessage(IntPtr hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMessage = unchecked((uint)lParam);
            if (mouseMessage == WM_LBUTTONUP)
                DispatcherQueue.TryEnqueue(RestoreFromTray);
            else if (mouseMessage == WM_RBUTTONUP)
                DispatcherQueue.TryEnqueue(ShowTrayMenu);
            return IntPtr.Zero;
        }

        if (!_isShuttingDown && msg == WM_CLOSE && App.Settings.MinimiseToTray)
        {
            DispatcherQueue.TryEnqueue(EnsureTrayIcon);
            ShowWindow(hwnd, 0);
            _hiddenToTray = true;
            return IntPtr.Zero;
        }

        if (msg == WM_CLOSE && !App.Settings.MinimiseToTray)
        {
            // Dispose media before WinUI tears down MediaPlayerElement.
            _isShuttingDown = true;
            _gamepadInputTimer.Stop();
            try
            {
                if (ContentFrame.Content is PlayerPage playerPage)
                    playerPage.PrepareForAppShutdown();
                else if (ContentFrame.Content is MusicPlayerPage musicPlayerPage)
                    musicPlayerPage.PrepareForAppShutdown();
                App.MusicPlayback.Shutdown();
            }
            catch { }
        }

        return _oldWindowProc != IntPtr.Zero
            ? CallWindowProc(_oldWindowProc, hwnd, msg, wParam, lParam)
            : IntPtr.Zero;
    }

    private void SetWindowIcon()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Lumen.ico");
            if (File.Exists(iconPath))
                appWindow.SetIcon(iconPath);
        }
        catch
        {
        }
    }

    private void MaximizeOnStartup()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter)
                presenter.Maximize();
        }
        catch
        {
        }
    }

    private void ProfileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(31, 149, 103, 232));
    }

    private void ProfileCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
            card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0, 255, 255, 255));
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= Root_Loaded;
        ShowLoading();

        await App.MainViewModel.InitializeAsync();
        await App.Downloads.LoadAsync();
        RefreshQuickDownloadButton();

        if (App.Jellyfin.IsAuthenticated)
        {
            ShowShell();
        }
        else if (App.Settings.OfflineStartupEnabled && App.Downloads.CompletedCount > 0)
        {
            ShowOfflineShell();
        }
        else
        {
            ShowLogin();
        }

        _reconnectTimer.Start();
    }

    private void LoginView_LoginSucceeded(object? sender, EventArgs e)
    {
        ShowShell();
    }

    private void LoginView_LoginCancelled(object? sender, EventArgs e)
    {
        if (App.Jellyfin.IsAuthenticated)
            ShowShell(navigateHome: false);
        else
            ShowLogin();
    }

    private void ShowLoading()
    {
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Collapsed;
        ShellRoot.Visibility = Visibility.Collapsed;
        LoadingRoot.Visibility = Visibility.Visible;
    }

    private void ShowLogin(bool allowCancel = false)
    {
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
        LoadingRoot.Visibility = Visibility.Collapsed;
        ShellRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Visible;
        LoginView.ShowCancel(allowCancel && App.Jellyfin.IsAuthenticated);
        LoginView.RefreshSavedServers();
    }

    private void ShowShell(bool navigateHome = true)
    {
        _offlineShellMode = false;
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
        LoadingRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Collapsed;
        ShellRoot.Visibility = Visibility.Visible;
        OfflineModeBadge.Visibility = Visibility.Collapsed;
        HomeNavButton.IsEnabled = true;
        SearchNavButton.IsEnabled = true;
        LibraryNavigationList.IsEnabled = true;
        TopSearchBorder.Visibility = ContentFrame.Content is SearchPage ? Visibility.Collapsed : Visibility.Visible;
        if (navigateHome || ContentFrame.Content is null)
        {
            ContentFrame.Navigate(typeof(HomePage));
            App.MainViewModel.PageTitle = "Home";
            BackButton.Visibility = Visibility.Collapsed;
        }
        _ = JumpListService.TryConfigureAsync();
        ApplyPendingLaunchArgument();
    }

    private void ShowOfflineShell()
    {
        _offlineShellMode = true;
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
        LoadingRoot.Visibility = Visibility.Collapsed;
        LoginRoot.Visibility = Visibility.Collapsed;
        ShellRoot.Visibility = Visibility.Visible;
        OfflineModeBadge.Visibility = Visibility.Visible;
        HomeNavButton.IsEnabled = false;
        SearchNavButton.IsEnabled = false;
        LibraryNavigationList.IsEnabled = false;
        TopSearchBorder.Visibility = Visibility.Collapsed;
        App.MainViewModel.ConnectionStatus = "Offline • downloaded media available";
        ContentFrame.Navigate(typeof(DownloadsPage));
        ContentFrame.BackStack.Clear();
        App.MainViewModel.PageTitle = "Downloads";
        BackButton.Visibility = Visibility.Collapsed;
        RefreshQuickDownloadButton();
    }

    private void CommandPalette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CommandPalettePanel.Visibility == Visibility.Visible)
            CloseCommandPalette();
        else
            OpenCommandPalette();
        args.Handled = true;
    }

    private void OpenCommandPalette()
    {
        CommandPalettePanel.Visibility = Visibility.Visible;
        CommandPaletteBox.Text = string.Empty;
        PopulatePaletteCommands();
        CommandPaletteBox.Focus(FocusState.Programmatic);
    }

    private void CloseCommandPalette()
    {
        _commandPaletteCts?.Cancel();
        CommandPalettePanel.Visibility = Visibility.Collapsed;
        CommandPaletteBox.Text = string.Empty;
    }

    private void PopulatePaletteCommands()
    {
        _commandPaletteItems.Clear();
        _commandPaletteItems.Add(new CommandPaletteResult { Title = "Home", Subtitle = "Go to Home", Glyph = "\uE80F", Command = "home" });
        _commandPaletteItems.Add(new CommandPaletteResult { Title = "Search", Subtitle = "Open full search", Glyph = "\uE721", Command = "search" });
        _commandPaletteItems.Add(new CommandPaletteResult { Title = "Settings", Subtitle = "Open Lumen settings", Glyph = "\uE713", Command = "settings" });
        foreach (var library in App.MainViewModel.Libraries.Take(8))
            _commandPaletteItems.Add(new CommandPaletteResult { Title = library.Name, Subtitle = "Library", Glyph = "\uE8B7", Command = $"library:{library.Id}" });
    }

    private async void CommandPaletteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = CommandPaletteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            PopulatePaletteCommands();
            return;
        }

        _commandPaletteCts?.Cancel();
        _commandPaletteCts?.Dispose();
        _commandPaletteCts = new CancellationTokenSource();
        var token = _commandPaletteCts.Token;

        try
        {
            await Task.Delay(180, token);
            var result = await App.Jellyfin.SearchItemsAsync(query, 12, token);
            if (token.IsCancellationRequested) return;

            _commandPaletteItems.Clear();
            foreach (var item in result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                _commandPaletteItems.Add(new CommandPaletteResult
                {
                    Title = item.Name ?? "Untitled",
                    Subtitle = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesName)
                        ? $"{item.SeriesName} • S{item.ParentIndexNumber:00}E{item.IndexNumber:00}"
                        : $"{item.ProductionYear} {item.Type}".Trim(),
                    Glyph = string.Equals(item.Type, "Person", StringComparison.OrdinalIgnoreCase) ? "\uE77B" : "\uE8B2",
                    ItemId = item.Id,
                    ItemType = item.Type,
                    ImageUrl = !string.IsNullOrWhiteSpace(item.Id)
                        ? App.Jellyfin.GetImageUrl(item.Id!, "Primary", item.PrimaryImageTag, 160, 88)
                        : string.Empty
                });
            }

            if ("settings".Contains(query, StringComparison.OrdinalIgnoreCase))
                _commandPaletteItems.Insert(0, new CommandPaletteResult { Title = "Settings", Subtitle = "Lumen command", Glyph = "\uE713", Command = "settings" });
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void CommandPaletteBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter &&
                 CommandPaletteResults.SelectedItem is CommandPaletteResult selected)
        {
            ActivatePaletteResult(selected);
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Down && CommandPaletteResults.Items.Count > 0)
        {
            CommandPaletteResults.SelectedIndex = Math.Min(CommandPaletteResults.SelectedIndex + 1, CommandPaletteResults.Items.Count - 1);
            e.Handled = true;
        }
    }

    private void CommandPaletteResults_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommandPaletteResult result)
            ActivatePaletteResult(result);
    }

    private void ActivatePaletteResult(CommandPaletteResult result)
    {
        CloseCommandPalette();
        if (!string.IsNullOrWhiteSpace(result.ItemId))
        {
            if (string.Equals(result.ItemType, "Person", StringComparison.OrdinalIgnoreCase))
            {
                ContentFrame.Navigate(typeof(PersonPage),
                    new PersonNavigationParameter(result.ItemId, result.Title, result.ImageUrl ?? string.Empty));
            }
            else if (string.Equals(result.ItemType, "MusicArtist", StringComparison.OrdinalIgnoreCase))
            {
                ContentFrame.Navigate(typeof(ArtistPage), result.ItemId);
            }
            else if (string.Equals(result.ItemType, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
            {
                ContentFrame.Navigate(typeof(MusicAlbumPage), result.ItemId);
            }
            else if (string.Equals(result.ItemType, "Audio", StringComparison.OrdinalIgnoreCase))
            {
                ContentFrame.Navigate(typeof(MusicPlayerPage), result.ItemId);
            }
            else
            {
                ContentFrame.Navigate(typeof(ItemPage), result.ItemId);
            }
            App.MainViewModel.PageTitle = result.Title;
            return;
        }

        switch (result.Command)
        {
            case "home":
                ContentFrame.Navigate(typeof(HomePage));
                App.MainViewModel.PageTitle = "Home";
                break;
            case "search":
                ContentFrame.Navigate(typeof(SearchPage));
                App.MainViewModel.PageTitle = "Search";
                break;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage));
                App.MainViewModel.PageTitle = "Settings";
                break;
            default:
                if (result.Command?.StartsWith("library:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var id = result.Command["library:".Length..];
                    var library = App.MainViewModel.Libraries.FirstOrDefault(x => x.Id == id);
                    if (library is not null)
                    {
                        ContentFrame.Navigate(typeof(LibraryPage), library);
                        App.MainViewModel.PageTitle = library.Name;
                    }
                }
                break;
        }
    }

    private async void Profile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        await ShowAccountPickerAsync();
    }

    private async Task ShowAccountPickerAsync()
    {
        _accountPickerItems.Clear();

        var publicUsers = new List<UserDto>();
        try { publicUsers = await App.Jellyfin.GetPublicUsersAsync(); } catch { }

        var currentServer = App.Jellyfin.BaseUrl.Trim().TrimEnd('/');
        var rememberedUsers = App.Settings.RememberedUsers
            .Where(x => string.Equals(
                x.ServerUrl.Trim().TrimEnd('/'),
                currentServer,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Hidden Jellyfin users may exist only in remembered profiles.
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var user in publicUsers
                     .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
                     .OrderBy(x => x.Name))
        {
            var remembered = rememberedUsers.FirstOrDefault(x =>
                string.Equals(x.UserId, user.Id, StringComparison.OrdinalIgnoreCase));

            var isCurrent = string.Equals(user.Id, App.Jellyfin.UserId, StringComparison.OrdinalIgnoreCase);
            // User avatars use /Users/{id}/Images/Primary.
            var imageUrl = App.Jellyfin.GetUserImageUrl(user.Id!, user.PrimaryImageTag, 180, 90);

            if (isCurrent && !string.IsNullOrWhiteSpace(App.MainViewModel.UserImageUrl))
                imageUrl = App.MainViewModel.UserImageUrl;

            _accountPickerItems.Add(new AccountPickerItem
            {
                UserId = user.Id!,
                Name = user.Name!,
                ImageUrl = imageUrl,
                IsCurrent = isCurrent,
                IsRemembered = remembered is not null
            });
            ids.Add(user.Id!);
        }

        foreach (var account in rememberedUsers
                     .Where(x => !string.IsNullOrWhiteSpace(x.UserId) && !ids.Contains(x.UserId))
                     .OrderBy(x => x.Username))
        {
            var isCurrent = string.Equals(account.UserId, App.Jellyfin.UserId, StringComparison.OrdinalIgnoreCase);
            var imageUrl = isCurrent && !string.IsNullOrWhiteSpace(App.MainViewModel.UserImageUrl)
                ? App.MainViewModel.UserImageUrl
                : App.Jellyfin.GetUserImageUrl(account.UserId, null, 180, 90);

            _accountPickerItems.Add(new AccountPickerItem
            {
                UserId = account.UserId,
                Name = string.IsNullOrWhiteSpace(account.Username) ? "Profile" : account.Username,
                ImageUrl = imageUrl,
                IsCurrent = isCurrent,
                IsRemembered = true
            });
        }

        if (!string.IsNullOrWhiteSpace(App.Jellyfin.UserId) &&
            !_accountPickerItems.Any(x =>
                string.Equals(x.UserId, App.Jellyfin.UserId, StringComparison.OrdinalIgnoreCase)))
        {
            _accountPickerItems.Insert(0, new AccountPickerItem
            {
                UserId = App.Jellyfin.UserId!,
                Name = App.MainViewModel.UserName ?? "Current profile",
                ImageUrl = App.MainViewModel.UserImageUrl ?? string.Empty,
                IsCurrent = true,
                IsRemembered = true
            });
        }

        AccountPickerOverlay.Visibility = Visibility.Visible;
    }


    private void AccountAvatar_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
            image.Visibility = Visibility.Collapsed;
    }

    private async void AccountPickerUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AccountPickerItem item })
            return;

        if (item.IsCurrent)
        {
            AccountPickerOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        AccountPickerOverlay.Visibility = Visibility.Collapsed;

        var remembered = App.Settings.FindRememberedUser(App.Jellyfin.BaseUrl, item.UserId);
        if (remembered is not null)
        {
            ShowLoading();
            if (await App.MainViewModel.SwitchToRememberedUserAsync(remembered))
            {
                ShowShell();
                return;
            }
        }

        // Invalid remembered credentials fall back to the login form.
        LoginView.PrepareForUser(item.Name);
        ShowLogin(allowCancel: true);
    }

    private void AccountPickerAdd_Click(object sender, RoutedEventArgs e)
    {
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
        LoginView.PrepareForUser(string.Empty);
        ShowLogin(allowCancel: true);
    }

    private void AccountPickerCancel_Click(object sender, RoutedEventArgs e)
    {
        AccountPickerOverlay.Visibility = Visibility.Collapsed;
    }

    public void SetLaunchArgument(string? argument)
    {
        _pendingLaunchArgument = argument ?? string.Empty;
    }

    private void ApplyPendingLaunchArgument()
    {
        if (string.IsNullOrWhiteSpace(_pendingLaunchArgument)) return;
        var argument = _pendingLaunchArgument;
        _pendingLaunchArgument = string.Empty;

        if (argument == "lumen:search")
        {
            ContentFrame.Navigate(typeof(SearchPage));
            App.MainViewModel.PageTitle = "Search";
        }
        else if (argument == "lumen:settings")
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            App.MainViewModel.PageTitle = "Settings";
        }
        else if (argument.StartsWith("lumen:item:", StringComparison.OrdinalIgnoreCase))
        {
            var id = argument["lumen:item:".Length..];
            if (!string.IsNullOrWhiteSpace(id))
                ContentFrame.Navigate(typeof(ItemPage), id);
        }
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        var isMusicPage = ContentFrame.Content is MusicLibraryPage
            or MusicAlbumPage
            or ArtistPage
            or MusicPlaylistPage
            or MusicPlayerPage;

        if (e.Parameter is MusicLibraryNavigationRequest musicRequest)
        {
            _currentMusicLibrary = musicRequest.Library;
            MusicSidebarLibraryName.Text = musicRequest.Library.Name;
        }
        else if (ContentFrame.Content is MusicLibraryPage && e.Parameter is LibraryViewModel musicLibrary)
        {
            _currentMusicLibrary = musicLibrary;
            MusicSidebarLibraryName.Text = musicLibrary.Name;
        }

        SetMusicShellMode(isMusicPage);
        if (ContentFrame.Content is MusicAlbumPage) SetMusicNavigationSelection("Albums");
        else if (ContentFrame.Content is ArtistPage) SetMusicNavigationSelection("Artists");
        else if (ContentFrame.Content is MusicPlaylistPage) SetMusicNavigationSelection("Playlists");
        UpdateMiniPlayer();
        var isHome = ContentFrame.Content is HomePage;
        var isSearchPage = ContentFrame.Content is SearchPage;
        BackButton.Visibility = (!isHome && ContentFrame.CanGoBack) ? Visibility.Visible : Visibility.Collapsed;
        HeaderPageTitle.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;

        TopSearchBorder.Visibility = (_offlineShellMode || isSearchPage) ? Visibility.Collapsed : Visibility.Visible;


        DispatcherQueue.TryEnqueue(SeedGamepadFocus);
    }

    private void TopSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        TopSearchBorder.Background = ResourceBrush("LumenSearchFocusBrush");
    }

    private void TopSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        TopSearchBorder.Background = ResourceBrush("LumenSearchIdleBrush");
    }

    private static Brush ResourceBrush(string key)
        => Application.Current.Resources[key] as Brush
           ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private async void TopSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TopSearchBox.Text.Trim();
        _topSearchCts?.Cancel();
        _topSearchCts?.Dispose();

        if (query.Length < 2)
        {
            if (ContentFrame.Content is SearchPage searchPage)
                await searchPage.SetQueryAsync(query);
            return;
        }

        _topSearchCts = new CancellationTokenSource();
        var token = _topSearchCts.Token;
        try
        {
            await Task.Delay(300, token);
            await ShowLiveSearchAsync(query, token);
        }
        catch (OperationCanceledException) { }
    }

    private async void TopSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;

        var query = TopSearchBox.Text.Trim();
        if (query.Length < 2) return;

        _topSearchCts?.Cancel();
        await ShowLiveSearchAsync(query);
        e.Handled = true;
    }

    private async Task ShowLiveSearchAsync(string query, CancellationToken ct = default)
    {
        if (ContentFrame.Content is SearchPage searchPage)
        {
            await searchPage.SetQueryAsync(query, ct);
        }
        else
        {
            ContentFrame.Navigate(typeof(SearchPage), query);
            App.MainViewModel.PageTitle = "Search";
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (_offlineShellMode) return;
        if (ContentFrame.Content is HomePage) return;
        var homeIndex = -1;
        for (var i = ContentFrame.BackStack.Count - 1; i >= 0; i--)
            if (ContentFrame.BackStack[i].SourcePageType == typeof(HomePage)) { homeIndex = i; break; }
        if (homeIndex >= 0)
        {
            while (ContentFrame.BackStack.Count - 1 > homeIndex)
                ContentFrame.BackStack.RemoveAt(ContentFrame.BackStack.Count - 1);
            ContentFrame.GoBack();
        }
        else ContentFrame.Navigate(typeof(HomePage));
        App.MainViewModel.PageTitle = "Home";
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_offlineShellMode || ContentFrame.Content is SearchPage) return;
        ContentFrame.Navigate(typeof(SearchPage));
        App.MainViewModel.PageTitle = "Search";
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is DownloadsPage) return;
        ContentFrame.Navigate(typeof(DownloadsPage));
        App.MainViewModel.PageTitle = "Downloads";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.Content is SettingsPage) return;
        ContentFrame.Navigate(typeof(SettingsPage));
        App.MainViewModel.PageTitle = "Settings";
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        if (_offlineShellMode) return;
        if (sender is Button { Tag: LibraryViewModel library })
        {
            var isMusic = string.Equals(library.Type, "music", StringComparison.OrdinalIgnoreCase);
            if (isMusic)
            {
                _currentMusicLibrary = library;
                MusicSidebarLibraryName.Text = library.Name;
                ContentFrame.Navigate(typeof(MusicLibraryPage), new MusicLibraryNavigationRequest(library, "Home"));
            }
            else
            {
                ContentFrame.Navigate(typeof(LibraryPage), library);
            }
            App.MainViewModel.PageTitle = library.Name;
        }
    }

    private void SetMusicShellMode(bool enabled)
    {
        _musicShellMode = enabled;
        if (IsFullScreen) return;
        SidebarColumn.Width = new GridLength(248);
        SidebarPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        MusicSidebarPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) SetMusicNavigationSelection(string.Empty);
    }

    public void SetMusicNavigationSelection(string activeView)
    {
        foreach (var button in new[]
        {
            MusicShellHomeButton, MusicShellRecentlyAddedButton, MusicShellArtistsButton,
            MusicShellAlbumsButton, MusicShellSongsButton, MusicShellPlaylistsButton,
            MusicShellGenresButton, MusicShellFavouritesButton
        })
        {
            var active = string.Equals(button.Tag as string, activeView, StringComparison.OrdinalIgnoreCase);
            button.Background = active
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenSelectedBrush"]
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.BorderBrush = active
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenBorderStrongBrush"]
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.BorderThickness = active ? new Thickness(1) : new Thickness(1);
            button.Foreground = active
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenTextBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LumenSecondaryTextBrush"];
        }
    }

    private void MusicShellNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string view } || _currentMusicLibrary is null) return;

        if (ContentFrame.Content is MusicLibraryPage musicPage)
        {
            musicPage.ShowViewFromShell(view);
        }
        else
        {
            ContentFrame.Navigate(typeof(MusicLibraryPage),
                new MusicLibraryNavigationRequest(_currentMusicLibrary, view));
        }
        App.MainViewModel.PageTitle = _currentMusicLibrary.Name;
    }

    private async void MusicShellShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMusicLibrary is null) return;
        try
        {
            var result = await App.Jellyfin.GetMusicItemsAsync(_currentMusicLibrary.Id, "Songs", 2500);
            var songs = result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
            if (songs.Count > 0)
                await App.MusicPlayback.PlayQueueAsync(songs, 0, shuffle: true);
        }
        catch { }
    }

    private void ReturnToLumen_Click(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(HomePage));
        ContentFrame.BackStack.Clear();
        App.MainViewModel.PageTitle = "Home";
    }

    // Kept for stale generated XAML.
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        Settings_Click(sender, e);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
            return;
        }

        if (!_offlineShellMode && ContentFrame.Content is not HomePage)
        {
            ContentFrame.Navigate(typeof(HomePage));
            ContentFrame.BackStack.Clear();
            App.MainViewModel.PageTitle = "Home";
        }
    }

    public async Task ChangeServerAsync()
    {
        await App.MainViewModel.ForgetSessionAsync();
        LoginView.ShowCancel(true);
        ShowLogin(allowCancel: true);
    }

    public void ShowLoginFromSettings()
    {
        ShowLogin(allowCancel: true);
    }
    public bool IsFullScreen
    {
        get
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                return appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            }
            catch { return false; }
        }
    }

    public bool ToggleFullScreen()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var enter = appWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
            SetPlayerChromeFullScreen(enter);
            appWindow.SetPresenter(enter ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
            return enter;
        }
        catch
        {
            SetPlayerChromeFullScreen(false);
            return false;
        }
    }

    private void SetPlayerChromeFullScreen(bool fullScreen)
    {
        // Hide shell chrome while PlayerPage owns fullscreen.
        SidebarColumn.Width = fullScreen ? new GridLength(0) : new GridLength(248);
        if (fullScreen)
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
            MusicSidebarPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarPanel.Visibility = _musicShellMode ? Visibility.Collapsed : Visibility.Visible;
            MusicSidebarPanel.Visibility = _musicShellMode ? Visibility.Visible : Visibility.Collapsed;
        }
        ShellHeader.Visibility = fullScreen ? Visibility.Collapsed : Visibility.Visible;
        ShellContentHost.Padding = fullScreen ? new Thickness(0) : new Thickness(34, 28, 34, 28);
        ContentFrame.Margin = fullScreen ? new Thickness(0) : new Thickness(0, 22, 0, 0);
    }

    public void ExitFullScreen()
    {
        try
        {
            SetPlayerChromeFullScreen(false);
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                appWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
        catch
        {
            SetPlayerChromeFullScreen(false);
        }
    }


    private async void ReconnectTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_reconnectCheckRunning || !App.MainViewModel.AutoReconnect)
            return;

        _reconnectCheckRunning = true;
        try
        {
            var restored = await App.MainViewModel.TryReconnectAsync();
            if (restored)
            {
                if (_offlineShellMode)
                    ShowShell(navigateHome: false);
                else if (ShellRoot.Visibility != Visibility.Visible)
                    ShowShell();
            }
        }
        finally
        {
            _reconnectCheckRunning = false;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isShuttingDown = true;
        _gamepadInputTimer.Stop();
        _gamepadInputTimer.Tick -= GamepadInputTimer_Tick;
        _miniPlayerTimer.Stop();
        _miniPlayerTimer.Tick -= MiniPlayerTimer_Tick;
        App.MusicPlayback.Changed -= MusicPlayback_Changed;
        try { App.MusicPlayback.Shutdown(); } catch { }
        RemoveTrayIcon();
        _reconnectTimer.Stop();

        try
        {
            if (_oldWindowProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
            {
                SetWindowProc(_hwnd, _oldWindowProc);
                _oldWindowProc = IntPtr.Zero;
            }
        }
        catch { }

        // PlayerPage tears down media before WinUI destroys the XAML tree.
    }

}
