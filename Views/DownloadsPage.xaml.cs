using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Lumen.Models;
using Lumen.Services;

namespace Lumen.Views;

public sealed partial class DownloadsPage : Page
{
    private readonly ObservableCollection<DownloadEntry> _visibleDownloads = [];
    private DownloadFilter _currentFilter = DownloadFilter.All;

    private enum DownloadFilter
    {
        All,
        Active,
        Completed,
        Failed
    }

    public DownloadsPage()
    {
        InitializeComponent();
        DownloadsList.ItemsSource = _visibleDownloads;
        Loaded += DownloadsPage_Loaded;
        Unloaded += DownloadsPage_Unloaded;
    }

    private async void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Downloads.LoadAsync();
        App.Downloads.Items.CollectionChanged += Downloads_CollectionChanged;
        foreach (var entry in App.Downloads.Items)
            entry.PropertyChanged += Entry_PropertyChanged;
        RefreshUi(rebuildList: true);
    }

    private void DownloadsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Downloads.Items.CollectionChanged -= Downloads_CollectionChanged;
        foreach (var entry in App.Downloads.Items)
            entry.PropertyChanged -= Entry_PropertyChanged;
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DownloadEntry entry in e.OldItems)
                entry.PropertyChanged -= Entry_PropertyChanged;
        if (e.NewItems is not null)
            foreach (DownloadEntry entry in e.NewItems)
                entry.PropertyChanged += Entry_PropertyChanged;
        RefreshUi(rebuildList: true);
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var needsFilterRefresh = e.PropertyName is nameof(DownloadEntry.Status)
            or nameof(DownloadEntry.IsComplete);
        var needsDashboardRefresh = needsFilterRefresh
            || e.PropertyName is nameof(DownloadEntry.Progress)
                or nameof(DownloadEntry.DownloadedBytes)
                or nameof(DownloadEntry.ExpectedBytes);

        if (!needsDashboardRefresh)
            return;

        if (DispatcherQueue.HasThreadAccess)
            RefreshUi(needsFilterRefresh);
        else
            DispatcherQueue.TryEnqueue(() => RefreshUi(needsFilterRefresh));
    }

    private void RefreshUi(bool rebuildList)
    {
        if (rebuildList)
            ApplyFilter();

        var all = App.Downloads.Items;
        var active = all.Count(IsActive);
        var completed = all.Count(x => x.IsComplete);
        var failed = all.Count(IsFailed);
        var onDisk = all.Sum(x => Math.Max(0, x.DownloadedBytes));

        ActiveStatText.Text = active.ToString();
        CompletedStatText.Text = completed.ToString();
        OnDiskStatText.Text = DownloadManager.FormatBytes(onDisk);

        AllFilterText.Text = $"All  {all.Count}";
        ActiveFilterText.Text = $"Downloading  {active}";
        CompletedFilterText.Text = $"Completed  {completed}";
        FailedFilterText.Text = $"Failed  {failed}";
        UpdateFilterButtonStates();

        var storage = App.Downloads.GetStorageInfo();
        FolderPathText.Text = storage.Path;

        if (storage.TotalBytes > 0)
        {
            var used = Math.Max(0, storage.TotalBytes - storage.AvailableBytes);
            var usedPercent = Math.Clamp(used * 100d / storage.TotalBytes, 0, 100);
            StorageProgressBar.Value = usedPercent;
            StorageProgressBar.Visibility = Visibility.Visible;
            StoragePercentText.Text = $"{usedPercent:0}% used";
            StorageText.Text = $"{DownloadManager.FormatBytes(used)} used  •  {DownloadManager.FormatBytes(storage.AvailableBytes)} free of {DownloadManager.FormatBytes(storage.TotalBytes)}";
        }
        else
        {
            StorageProgressBar.Value = 0;
            StorageProgressBar.Visibility = Visibility.Collapsed;
            StoragePercentText.Text = string.Empty;
            StorageText.Text = "Storage information unavailable";
        }

        var empty = _visibleDownloads.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        DownloadsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (all.Count == 0)
        {
            EmptyTitleText.Text = "Nothing downloaded yet";
            EmptySubtitleText.Text = "Download a movie or episode from its details page and it will appear here.";
        }
        else if (empty)
        {
            EmptyTitleText.Text = _currentFilter switch
            {
                DownloadFilter.Active => "No active downloads",
                DownloadFilter.Completed => "No completed downloads",
                DownloadFilter.Failed => "No failed downloads",
                _ => "No downloads"
            };
            EmptySubtitleText.Text = "Choose another filter to see the rest of your offline library.";
        }
    }

    private void ApplyFilter()
    {
        _visibleDownloads.Clear();
        foreach (var entry in App.Downloads.Items.Where(MatchesCurrentFilter))
            _visibleDownloads.Add(entry);
    }

    private bool MatchesCurrentFilter(DownloadEntry entry) => _currentFilter switch
    {
        DownloadFilter.Active => IsActive(entry),
        DownloadFilter.Completed => entry.IsComplete,
        DownloadFilter.Failed => IsFailed(entry),
        _ => true
    };

    private static bool IsActive(DownloadEntry entry)
        => entry.Status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Queued", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Paused", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(DownloadEntry entry)
        => entry.Status.StartsWith("Download failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Ready to retry", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filterName ||
            !Enum.TryParse(filterName, ignoreCase: true, out DownloadFilter filter))
            return;

        _currentFilter = filter;
        RefreshUi(rebuildList: true);
    }

    private void UpdateFilterButtonStates()
    {
        SetFilterState(AllFilterButton, _currentFilter == DownloadFilter.All);
        SetFilterState(ActiveFilterButton, _currentFilter == DownloadFilter.Active);
        SetFilterState(CompletedFilterButton, _currentFilter == DownloadFilter.Completed);
        SetFilterState(FailedFilterButton, _currentFilter == DownloadFilter.Failed);
    }

    private static void SetFilterState(Button button, bool selected)
    {
        button.Background = ResourceBrush(selected ? "LumenSelectedBrush" : "LumenSurface2Brush");
        button.BorderBrush = ResourceBrush(selected ? "LumenBorderStrongBrush" : "LumenBorderBrush");
        button.Foreground = ResourceBrush(selected ? "LumenTextBrush" : "LumenSecondaryTextBrush");
    }

    private static Brush ResourceBrush(string key)
        => (Brush)Application.Current.Resources[key];

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry } retryButton)
            return;

        if (App.Downloads.IsDownloading(entry.ItemId) ||
            entry.Status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
            return;

        retryButton.IsEnabled = false;
        try
        {
            var item = await App.Jellyfin.GetItemAsync(entry.ItemId);
            if (item is not null)
                await App.Downloads.DownloadAsync(item, entry.QualityId);
        }
        catch { }
        finally
        {
            retryButton.IsEnabled = true;
            RefreshUi(rebuildList: true);
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry })
            App.Downloads.Pause(entry);
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry } button)
            return;

        button.IsEnabled = false;
        try { await App.Downloads.ResumeAsync(entry); }
        catch { }
        finally
        {
            button.IsEnabled = true;
            RefreshUi(rebuildList: true);
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry })
            return;

        var dialog = new ContentDialog
        {
            Title = "Cancel download?",
            Content = $"Cancel “{entry.Title}” and delete its partial download?",
            PrimaryButtonText = "Cancel download",
            CloseButtonText = "Keep downloading",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            App.Downloads.Cancel(entry);
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadEntry entry } && entry.IsComplete && File.Exists(entry.LocalPath))
        {
            Frame.Navigate(typeof(PlayerPage), new PlaybackSelection
            {
                ItemId = entry.ItemId,
                LocalFilePath = entry.LocalPath,
                IsOffline = true
            });
        }
    }

    private async void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadEntry entry } button)
            return;

        var flyout = new MenuFlyout();

        if (entry.IsComplete && File.Exists(entry.LocalPath))
        {
            var showInFolder = new MenuFlyoutItem
            {
                Text = "Show in folder",
                Icon = new FontIcon { Glyph = "\uE8E5", FontFamily = new FontFamily("Segoe Fluent Icons") }
            };
            showInFolder.Click += async (_, _) => await OpenEntryFolderAsync(entry);
            flyout.Items.Add(showInFolder);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var delete = new MenuFlyoutItem
        {
            Text = entry.IsComplete ? "Delete download" : "Remove from downloads",
            Icon = new FontIcon { Glyph = "\uE74D", FontFamily = new FontFamily("Segoe Fluent Icons") }
        };
        delete.Click += async (_, _) => await ConfirmAndRemoveAsync(entry);
        flyout.Items.Add(delete);

        flyout.ShowAt(button);
    }

    private async Task OpenEntryFolderAsync(DownloadEntry entry)
    {
        if (!File.Exists(entry.LocalPath))
            return;

        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(entry.LocalPath)!);
            var options = new Windows.System.FolderLauncherOptions();
            options.ItemsToSelect.Add(await folder.GetFileAsync(Path.GetFileName(entry.LocalPath)));
            await Windows.System.Launcher.LaunchFolderAsync(folder, options);
        }
        catch { }
    }

    private async void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = App.Downloads.GetConfiguredDownloadRoot();
            Directory.CreateDirectory(path);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch { }
    }

    private async void ChangeDownloadFolder_Click(object sender, RoutedEventArgs e)
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
            RefreshUi(rebuildList: false);
        }
        catch { }
    }

    private async void ResetDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.DownloadFolder = string.Empty;
        await App.Settings.SaveAsync();
        RefreshUi(rebuildList: false);
    }

    private async Task ConfirmAndRemoveAsync(DownloadEntry entry)
    {
        var dialog = new ContentDialog
        {
            Title = entry.IsComplete ? "Delete download?" : "Remove download?",
            Content = entry.IsComplete
                ? $"Delete the local copy of “{entry.Title}”?"
                : $"Remove “{entry.Title}” and any partial file from Downloads?",
            PrimaryButtonText = entry.IsComplete ? "Delete" : "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await App.Downloads.RemoveAsync(entry);
        RefreshUi(rebuildList: true);
    }

    private async void ClearFailed_Click(object sender, RoutedEventArgs e)
    {
        var failed = App.Downloads.Items.Count(IsFailed);
        if (failed == 0)
            return;

        var dialog = new ContentDialog
        {
            Title = "Clear failed downloads?",
            Content = $"Remove {failed} failed or cancelled download{(failed == 1 ? "" : "s")} and any partial files?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await App.Downloads.ClearFailedAndCancelledAsync();
        RefreshUi(rebuildList: true);
    }

    private async void DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        var inactive = App.Downloads.Items.Count(x => !App.Downloads.IsDownloading(x.ItemId));
        if (inactive == 0)
            return;

        var dialog = new ContentDialog
        {
            Title = "Delete all downloads?",
            Content = $"Delete {inactive} local download{(inactive == 1 ? "" : "s")}? Active transfers will be left alone.",
            PrimaryButtonText = "Delete all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await App.Downloads.RemoveAllInactiveAsync();
        RefreshUi(rebuildList: true);
    }
}
