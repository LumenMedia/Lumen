using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Lumen;

namespace Lumen.Views;

public sealed partial class SettingsPage : Page
{
    private bool _navigatingToSection;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
        SettingsScroller.ViewChanged += SettingsScroller_ViewChanged;
        DataContext = App.MainViewModel;
    }


    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsPage_Loaded;
        foreach (var item in DownloadConcurrencyComboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == App.Settings.DownloadConcurrency)
            {
                DownloadConcurrencyComboBox.SelectedItem = item;
                break;
            }
        }
        if (DownloadConcurrencyComboBox.SelectedItem is null)
            DownloadConcurrencyComboBox.SelectedIndex = 1;

        foreach (var item in DownloadQualityComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), App.Settings.DownloadQuality, StringComparison.OrdinalIgnoreCase))
            {
                DownloadQualityComboBox.SelectedItem = item;
                break;
            }
        }
        if (DownloadQualityComboBox.SelectedItem is null)
            DownloadQualityComboBox.SelectedIndex = 0;

        RefreshDownloadStorageUi();
        OfflineDownloadCountText.Text = $"{App.Downloads.CompletedCount} download{(App.Downloads.CompletedCount == 1 ? "" : "s")}";
        var adjustedTracks = App.Settings.LyricOffsets.Keys
            .Concat(App.Settings.LyricScales.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        MusicSyncStatus.Text = adjustedTracks == 0
            ? "No saved manual timing adjustments."
            : $"{adjustedTracks} track{(adjustedTracks == 1 ? "" : "s")} with saved timing adjustments.";
        UpdateActiveSettingsNavFromScroll();
    }

    private void SettingsScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Keep the clicked destination active during animated scrolling.
        if (_navigatingToSection && e.IsIntermediate)
            return;

        if (_navigatingToSection)
            _navigatingToSection = false;

        UpdateActiveSettingsNavFromScroll();
    }

    private void UpdateActiveSettingsNavFromScroll()
    {
        if (!IsLoaded || SettingsScrollContent is null)
            return;

        var sections = new (FrameworkElement Section, Button Button)[]
        {
            (GeneralSection, NavGeneral),
            (PlaybackSection, NavPlayback),
            (HomeSection, NavHome),
            (SubtitleSection, NavSubtitles),
            (MusicSection, NavMusic),
            (DownloadsSection, NavDownloads),
            (IntegrationsSection, NavIntegrations),
            (AdvancedSection, NavAdvanced)
        };

        // Track the section just below the top of the viewport.
        var markerY = SettingsScroller.VerticalOffset + 96;
        var active = NavGeneral;

        foreach (var (section, button) in sections)
        {
            var point = section.TransformToVisual(SettingsScrollContent)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            if (point.Y <= markerY)
                active = button;
            else
                break;
        }

        // The final section wins when the scroll reaches the bottom.
        var atBottom = SettingsScroller.ScrollableHeight > 0 &&
                       SettingsScroller.VerticalOffset >= SettingsScroller.ScrollableHeight - 2;
        if (atBottom)
            active = NavAdvanced;

        SetActiveSettingsNav(active);
    }

    private void SettingsNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string targetName } button)
            return;

        if (FindName(targetName) is FrameworkElement target)
        {
            var point = target.TransformToVisual(SettingsScrollContent)
                .TransformPoint(new Windows.Foundation.Point(0, 0));
            _navigatingToSection = true;
            SetActiveSettingsNav(button);
            SettingsScroller.ChangeView(null, Math.Max(0, point.Y - 8), null, false);
        }
    }

    private void SetActiveSettingsNav(Button active)
    {
        foreach (var button in new[]
        {
            NavGeneral, NavPlayback, NavHome, NavSubtitles, NavMusic,
            NavDownloads, NavIntegrations, NavAdvanced
        })
        {
            var selected = ReferenceEquals(button, active);
            button.Background = selected
                ? (Brush)Application.Current.Resources["LumenSelectedBrush"]
                : (Brush)Application.Current.Resources["LumenTransparentBrush"];
            button.BorderBrush = selected
                ? (Brush)Application.Current.Resources["LumenBorderStrongBrush"]
                : (Brush)Application.Current.Resources["LumenTransparentBrush"];
            button.Foreground = selected
                ? (Brush)Application.Current.Resources["LumenTextBrush"]
                : (Brush)Application.Current.Resources["LumenSecondaryTextBrush"];
        }
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(DownloadsPage));
        App.MainViewModel.PageTitle = "Downloads";
    }

    private void OpenMusic_Click(object sender, RoutedEventArgs e)
    {
        var library = App.MainViewModel.Libraries.FirstOrDefault(x =>
            string.Equals(x.Type, "music", StringComparison.OrdinalIgnoreCase));
        if (library is null)
        {
            MusicSyncStatus.Text = "No music library is available for this account.";
            return;
        }

        Frame.Navigate(typeof(MusicLibraryPage), new MusicLibraryNavigationRequest(library, "Home"));
        App.MainViewModel.PageTitle = library.Name;
    }

    private async void ResetLyricAdjustments_Click(object sender, RoutedEventArgs e)
    {
        if (App.Settings.LyricOffsets.Count == 0 && App.Settings.LyricScales.Count == 0)
        {
            MusicSyncStatus.Text = "There are no saved lyric timing adjustments.";
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Reset lyric timing adjustments?",
            Content = "This clears all manual lyric offsets and timing scales saved by Lumen. AutoSync and LRCLIB lyrics are not removed.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        App.Settings.LyricOffsets.Clear();
        App.Settings.LyricScales.Clear();
        await App.Settings.SaveAsync();
        MusicSyncStatus.Text = "Saved lyric timing adjustments cleared.";
    }

    private async void DownloadQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || DownloadQualityComboBox.SelectedItem is not ComboBoxItem item)
            return;

        App.Settings.DownloadQuality = item.Tag?.ToString() ?? "Original";
        await App.Settings.SaveAsync();
    }

    private async void DownloadFolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
            };
            picker.FileTypeFilter.Add("*");

            if (App.MainWindow is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
                return;

            App.Settings.DownloadFolder = folder.Path;
            await App.Settings.SaveAsync();
            RefreshDownloadStorageUi();
        }
        catch (Exception ex)
        {
            DownloadStorageText.Text = $"Could not change the folder: {ex.Message}";
        }
    }

    private async void DownloadFolderReset_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DownloadFolder = string.Empty;
        await App.Settings.SaveAsync();
        RefreshDownloadStorageUi();
    }

    private void RefreshDownloadStorageUi()
    {
        var storage = App.Downloads.GetStorageInfo();
        DownloadFolderTextBox.Text = storage.Path;
        DownloadStorageText.Text = storage.Summary;
        if (OfflineDownloadCountText is not null)
            OfflineDownloadCountText.Text = $"{App.Downloads.CompletedCount} download{(App.Downloads.CompletedCount == 1 ? "" : "s")}";
    }

    private async void DownloadConcurrency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DownloadConcurrencyComboBox.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var value))
            return;
        App.Settings.DownloadConcurrency = Math.Clamp(value, 1, 8);
        await App.Settings.SaveAsync();
    }

    private async void ChangeServer_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow window)
            await window.ChangeServerAsync();
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        await App.MainViewModel.ForgetSessionAsync();
        if (App.MainWindow is MainWindow window)
            window.ShowLoginFromSettings();
    }

    private async void AutoLogin_Toggled(object sender, RoutedEventArgs e)
    {
        await App.MainViewModel.SaveSettingsAsync();
    }

    private async void Setting_Toggled(object sender, RoutedEventArgs e)
    {
        await App.MainViewModel.SaveSettingsAsync();
    }

    private async void Setting_Changed(object sender, SelectionChangedEventArgs e)
    {
        await App.MainViewModel.SaveSettingsAsync();
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        App.MainViewModel.ClearArtworkCache();
        DiagnosticsStatus.Text = "Artwork cache cleared.";
    }

    private async void ScanLibraries_Click(object sender, RoutedEventArgs e)
    {
        if (!App.MainViewModel.IsAdministrator)
            return;

        try
        {
            AdminStatus.Text = "Starting library scan…";
            await App.Jellyfin.RefreshLibraryAsync();
            AdminStatus.Text = "Library scan requested successfully.";
        }
        catch (Exception ex)
        {
            AdminStatus.Text = $"Could not start the library scan: {ex.Message}";
        }
    }

    private async void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (!App.MainViewModel.IsAdministrator || string.IsNullOrWhiteSpace(App.Jellyfin.BaseUrl))
            return;

        try
        {
            var uri = new Uri(App.Jellyfin.BaseUrl.TrimEnd('/') + "/web/#/dashboard.html");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            DiagnosticsStatus.Text = "Could not open the Jellyfin Dashboard.";
        }
    }

    private async void ConnectionInfo_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 440 };
        void Add(string name, string? value)
        {
            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = (Brush)Application.Current.Resources["LumenSecondaryTextBrush"]
            });
            var text = new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "Unknown" : value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            panel.Children.Add(grid);
        }

        Add("Server", App.MainViewModel.ServerName);
        Add("Endpoint", App.MainViewModel.ServerUrl);
        Add("Status", App.MainViewModel.ConnectionStatus);
        Add("Jellyfin version", App.MainViewModel.ServerVersion);
        Add("User", App.MainViewModel.UserName);
        Add("Lumen version", App.MainViewModel.AppVersion);

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await App.Jellyfin.GetPublicSystemInfoAsync();
            sw.Stop();
            Add("Server latency", $"{sw.ElapsedMilliseconds} ms");
        }
        catch
        {
            Add("Server latency", "Unreachable");
        }

        var dialog = new ContentDialog
        {
            Title = "Connection information",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var text = App.MainViewModel.GetDiagnosticsText();
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        await Task.Delay(1);
        DiagnosticsStatus.Text = "Diagnostics copied to the clipboard.";
    }
}
