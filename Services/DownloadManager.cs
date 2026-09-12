using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;
using Microsoft.UI.Xaml;

namespace Lumen.Services;

public sealed record DownloadQualityOption(
    string Id,
    string Label,
    string Description,
    int? MaxHeight,
    int? VideoBitrate,
    int AudioBitrate = 192_000)
{
    public bool IsOriginal => string.Equals(Id, "Original", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Label;
}

public sealed record DownloadStorageInfo(
    string Path,
    long AvailableBytes,
    long TotalBytes)
{
    public string Summary =>
        TotalBytes > 0
            ? $"{DownloadManager.FormatBytes(AvailableBytes)} free of {DownloadManager.FormatBytes(TotalBytes)}"
            : "Storage information unavailable";
}

public partial class DownloadEntry : ObservableObject
{
    [ObservableProperty]
    public partial string ItemId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string LocalPath { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial double Progress { get; set; }
    [ObservableProperty]
    public partial string Status { get; set; } = "Queued";
    [ObservableProperty]
    public partial bool IsComplete { get; set; }
    [ObservableProperty]
    public partial string SpeedText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string EtaText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string QualityId { get; set; } = "Original";
    [ObservableProperty]
    public partial long ExpectedBytes { get; set; }
    [ObservableProperty]
    public partial long DownloadedBytes { get; set; }
    [ObservableProperty]
    public partial Visibility RetryVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility PlayVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility RemoveVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility PauseVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility ResumeVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility CancelVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial Visibility TransferDetailsVisibility { get; set; } = Visibility.Collapsed;
    [ObservableProperty]
    public partial bool ShowInQuickHistory { get; set; } = true;

    public string QualityText => DownloadManager.GetQuality(QualityId).Label;

    public string SizeText
    {
        get
        {
            var actual = DownloadedBytes > 0 ? DownloadedBytes : ExpectedBytes;
            if (actual <= 0) return string.Empty;
            return IsComplete
                ? DownloadManager.FormatBytes(actual)
                : $"~{DownloadManager.FormatBytes(actual)}";
        }
    }

    public string TotalSizeText
    {
        get
        {
            var total = ExpectedBytes > 0 ? ExpectedBytes : DownloadedBytes;
            return total > 0 ? DownloadManager.FormatBytes(total) : "Size pending";
        }
    }

    public string ProgressText => $"{Math.Clamp(Progress, 0, 100):0}%";

    public string TransferAmountText
    {
        get
        {
            if (ExpectedBytes > 0)
                return $"{DownloadManager.FormatBytes(Math.Max(0, DownloadedBytes))} of {DownloadManager.FormatBytes(ExpectedBytes)}";
            if (DownloadedBytes > 0)
                return DownloadManager.FormatBytes(DownloadedBytes);
            return "Preparing transfer…";
        }
    }

    public string StatusDetailText
    {
        get
        {
            if (Status.StartsWith("Download failed • ", StringComparison.OrdinalIgnoreCase))
                return Status[18..];
            if (string.Equals(Status, "Ready to retry", StringComparison.OrdinalIgnoreCase))
                return "The local file is missing or incomplete. Retry to download it again.";
            if (string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return "This download was cancelled.";
            if (string.Equals(Status, "Paused", StringComparison.OrdinalIgnoreCase))
                return "Transfer paused. Resume when you are ready.";
            if (string.Equals(Status, "Queued", StringComparison.OrdinalIgnoreCase))
                return "Waiting for an available download slot.";
            if (string.Equals(Status, "Downloaded", StringComparison.OrdinalIgnoreCase))
                return "Available offline on this PC.";
            return string.Empty;
        }
    }

    partial void OnQualityIdChanged(string value) => OnPropertyChanged(nameof(QualityText));
    partial void OnExpectedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(TransferAmountText));
    }
    partial void OnDownloadedBytesChanged(long value)
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(TransferAmountText));
    }
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusDetailText));
    partial void OnIsCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TotalSizeText));
    }

    public void SetState(string state)
    {
        Status = state;

        var downloading = state.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase);
        var queued = string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase);
        var paused = string.Equals(state, "Paused", StringComparison.OrdinalIgnoreCase);
        var failed = state.StartsWith("Download failed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(state, "Ready to retry", StringComparison.OrdinalIgnoreCase);
        var complete = string.Equals(state, "Downloaded", StringComparison.OrdinalIgnoreCase);
        var cancelled = string.Equals(state, "Cancelled", StringComparison.OrdinalIgnoreCase);

        RetryVisibility = failed ? Visibility.Visible : Visibility.Collapsed;
        PlayVisibility = complete ? Visibility.Visible : Visibility.Collapsed;
        RemoveVisibility = (complete || failed || cancelled) ? Visibility.Visible : Visibility.Collapsed;
        PauseVisibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        ResumeVisibility = paused ? Visibility.Visible : Visibility.Collapsed;
        CancelVisibility = (queued || downloading || paused) ? Visibility.Visible : Visibility.Collapsed;
        TransferDetailsVisibility = (queued || downloading || paused) ? Visibility.Visible : Visibility.Collapsed;

        if (!downloading)
        {
            SpeedText = string.Empty;
            EtaText = string.Empty;
        }
    }
}

public sealed class DownloadManager
{
    private static readonly string MetadataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen", "Downloads");
    private static readonly string ManifestPath = Path.Combine(MetadataRoot, "downloads.json");
    private const long SafetyReserveBytes = 512L * 1024L * 1024L;

    public static IReadOnlyList<DownloadQualityOption> QualityOptions { get; } =
    [
        new("Original", "Original", "Download the original Jellyfin file without re-encoding.", null, null),
        new("High", "High • up to 1080p", "H.264/AAC MP4, up to 1080p at about 12 Mbps.", 1080, 12_000_000),
        new("Medium", "Medium • up to 720p", "H.264/AAC MP4, up to 720p at about 6 Mbps.", 720, 6_000_000),
        new("DataSaver", "Data saver • up to 480p", "H.264/AAC MP4, up to 480p at about 2.5 Mbps.", 480, 2_500_000)
    ];

    private readonly ConcurrentDictionary<string, Task<DownloadEntry>> _activeDownloads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadTokens =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _stopReasons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _manifestLock = new(1, 1);
    private readonly SemaphoreSlim _transferSlotSignal = new(0, int.MaxValue);
    private readonly object _transferSlotLock = new();
    private int _activeTransferCount;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _loaded;

    public ObservableCollection<DownloadEntry> Items { get; } = [];

    public static DownloadQualityOption GetQuality(string? id)
        => QualityOptions.FirstOrDefault(x =>
               string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? QualityOptions[0];

    public string GetConfiguredDownloadRoot()
    {
        var configured = App.Settings.DownloadFolder?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? MetadataRoot
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
    }

    public DownloadStorageInfo GetStorageInfo()
    {
        var root = GetConfiguredDownloadRoot();
        try
        {
            Directory.CreateDirectory(root);
            var full = Path.GetFullPath(root);
            var driveRoot = Path.GetPathRoot(full);
            if (!string.IsNullOrWhiteSpace(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                return new DownloadStorageInfo(root, drive.AvailableFreeSpace, drive.TotalSize);
            }
        }
        catch { }

        return new DownloadStorageInfo(root, 0, 0);
    }

    public long GetEstimatedBytes(BaseItemDto item, string? qualityId = null)
    {
        var quality = GetQuality(qualityId ?? App.Settings.DownloadQuality);
        if (quality.IsOriginal)
            return Math.Max(0, item.MediaSources?.FirstOrDefault()?.Size ?? 0);

        var runtimeTicks = Math.Max(0, item.RunTimeTicks ?? 0);
        if (runtimeTicks <= 0 || quality.VideoBitrate is not > 0)
            return 0;

        var seconds = TimeSpan.FromTicks(runtimeTicks).TotalSeconds;
        var bitsPerSecond = quality.VideoBitrate.Value + quality.AudioBitrate;
        return (long)Math.Ceiling((bitsPerSecond / 8d) * seconds * 1.04d);
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;

        await _loadLock.WaitAsync();
        try
        {
            if (_loaded) return;
            Directory.CreateDirectory(MetadataRoot);

            if (File.Exists(ManifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(ManifestPath);
                    var saved = JsonSerializer.Deserialize<List<DownloadEntry>>(json) ?? [];

                    Items.Clear();
                    foreach (var entry in saved.Where(x => !string.IsNullOrWhiteSpace(x.LocalPath)))
                    {
                        var partialPath = entry.LocalPath + ".part";
                        var hasPartial = File.Exists(partialPath) && new FileInfo(partialPath).Length > 0;
                        entry.DownloadedBytes = File.Exists(entry.LocalPath)
                            ? new FileInfo(entry.LocalPath).Length
                            : hasPartial ? new FileInfo(partialPath).Length : 0;
                        entry.IsComplete = File.Exists(entry.LocalPath);

                        if (entry.IsComplete)
                        {
                            entry.Progress = 100;
                            entry.SetState("Downloaded");
                        }
                        else if (hasPartial)
                        {
                            entry.SetState("Paused");
                        }
                        else if (!entry.Status.StartsWith("Download failed", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Progress = 0;
                            entry.SetState("Ready to retry");
                        }

                        Items.Add(entry);
                    }
                }
                catch { }
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<DownloadEntry> DownloadAsync(
        BaseItemDto item,
        string? qualityId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
            throw new InvalidOperationException("This item cannot be downloaded.");

        await LoadAsync();

        var requestedQuality = GetQuality(qualityId ?? App.Settings.DownloadQuality).Id;
        var activeTask = _activeDownloads.GetOrAdd(item.Id!, _ =>
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _downloadTokens[item.Id!] = cts;
            return DownloadCoreAsync(item, requestedQuality, cts.Token);
        });

        try
        {
            return await activeTask;
        }
        finally
        {
            if (activeTask.IsCompleted)
            {
                _activeDownloads.TryRemove(new KeyValuePair<string, Task<DownloadEntry>>(item.Id!, activeTask));
                if (_downloadTokens.TryRemove(item.Id!, out var cts))
                    cts.Dispose();
            }
        }
    }

    private async Task AcquireTransferSlotAsync(CancellationToken ct)
    {
        while (true)
        {
            lock (_transferSlotLock)
            {
                var limit = Math.Clamp(App.Settings.DownloadConcurrency, 1, 8);
                if (_activeTransferCount < limit)
                {
                    _activeTransferCount++;
                    return;
                }
            }
            await _transferSlotSignal.WaitAsync(ct);
        }
    }

    private void ReleaseTransferSlot()
    {
        lock (_transferSlotLock)
        {
            if (_activeTransferCount > 0)
                _activeTransferCount--;
        }
        _transferSlotSignal.Release();
    }

    public bool IsDownloading(string itemId)
        => !string.IsNullOrWhiteSpace(itemId) && _activeDownloads.ContainsKey(itemId);

    public DownloadEntry? GetCompletedDownload(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        return Items.FirstOrDefault(x =>
            string.Equals(x.ItemId, itemId, StringComparison.OrdinalIgnoreCase) &&
            x.IsComplete &&
            !string.IsNullOrWhiteSpace(x.LocalPath) &&
            File.Exists(x.LocalPath));
    }

    public bool HasCompletedDownload(string? itemId)
        => GetCompletedDownload(itemId) is not null;

    public int CompletedCount
        => Items.Count(x => x.IsComplete && !string.IsNullOrWhiteSpace(x.LocalPath) && File.Exists(x.LocalPath));

    private async Task<DownloadEntry> DownloadCoreAsync(
        BaseItemDto item,
        string qualityId,
        CancellationToken ct)
    {
        var root = GetConfiguredDownloadRoot();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(MetadataRoot);

        var quality = GetQuality(qualityId);
        var existing = Items.FirstOrDefault(x =>
            string.Equals(x.ItemId, item.Id, StringComparison.OrdinalIgnoreCase));

        if (existing is not null && existing.IsComplete && File.Exists(existing.LocalPath))
            return existing;

        var extension = quality.IsOriginal ? GetExtension(item) : ".mp4";
        var fileName = Sanitize($"{item.Name ?? item.Id}{extension}");
        var path = Path.Combine(root, item.Id!, fileName);

        if (existing is not null &&
            !string.Equals(existing.QualityId, quality.Id, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(existing.LocalPath))
        {
            try
            {
                var oldPartial = existing.LocalPath + ".part";
                if (File.Exists(oldPartial)) File.Delete(oldPartial);
            }
            catch { }
        }

        var entry = existing ?? new DownloadEntry
        {
            ItemId = item.Id!,
            Title = item.Name ?? "Untitled",
            Subtitle = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesName)
                ? $"{item.SeriesName} • S{item.ParentIndexNumber:00}E{item.IndexNumber:00}"
                : item.Type ?? string.Empty,
            ImageUrl = App.Jellyfin.GetImageUrl(item.Id!, "Primary", item.PrimaryImageTag, 320, 88)
        };

        entry.ShowInQuickHistory = true;
        entry.LocalPath = path;
        entry.QualityId = quality.Id;
        entry.ExpectedBytes = GetEstimatedBytes(item, quality.Id);
        entry.IsComplete = false;

        if (existing is null)
            Items.Insert(0, entry);

        var partialPath = path + ".part";
        var partialBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
        entry.DownloadedBytes = partialBytes;

        var storage = GetStorageInfo();
        var remainingEstimate = entry.ExpectedBytes > 0
            ? Math.Max(0, entry.ExpectedBytes - partialBytes)
            : SafetyReserveBytes;
        var requiredWithReserve = remainingEstimate + SafetyReserveBytes;

        if (storage.AvailableBytes > 0 && storage.AvailableBytes < requiredWithReserve)
        {
            entry.SetState(
                $"Download failed • Not enough free space. Need about {FormatBytes(requiredWithReserve)}, " +
                $"{FormatBytes(storage.AvailableBytes)} is available.");
            await SaveAsync();
            throw new IOException(entry.Status);
        }

        entry.SetState("Queued");
        entry.Progress = entry.ExpectedBytes > 0
            ? Math.Clamp(partialBytes * 100d / entry.ExpectedBytes, 0, 99.9)
            : 0;

        var progress = new Progress<JellyfinClient.DownloadTransferProgress>(value =>
        {
            entry.Progress = value.Percent;
            entry.SpeedText = FormatSpeed(value.BytesPerSecond);
            entry.EtaText = FormatEta(value.Eta);
            entry.DownloadedBytes = value.BytesTransferred;
            if (value.TotalBytes is > 0)
                entry.ExpectedBytes = value.TotalBytes.Value;
        });

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await SaveAsync();

            await AcquireTransferSlotAsync(ct);
            try
            {
                entry.SetState("Downloading");
                await App.Jellyfin.DownloadItemAsync(
                    item.Id!,
                    partialPath,
                    transcode: !quality.IsOriginal,
                    maxHeight: quality.MaxHeight,
                    videoBitrate: quality.VideoBitrate,
                    audioBitrate: quality.AudioBitrate,
                    progress: progress,
                    ct: ct);
            }
            finally
            {
                ReleaseTransferSlot();
            }

            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length <= 0)
                throw new IOException("Jellyfin returned an empty download.");

            if (File.Exists(path))
                File.Delete(path);

            await MoveCompletedDownloadAsync(partialPath, path, ct);

            entry.Progress = 100;
            entry.DownloadedBytes = new FileInfo(path).Length;
            entry.IsComplete = true;
            entry.SetState("Downloaded");
            await SaveAsync();
            return entry;
        }
        catch (OperationCanceledException) when (_stopReasons.TryRemove(item.Id!, out var reason))
        {
            entry.IsComplete = false;

            if (string.Equals(reason, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(partialPath))
                        File.Delete(partialPath);
                }
                catch { }

                entry.Progress = 0;
                entry.DownloadedBytes = 0;
                entry.SetState("Cancelled");
            }
            else
            {
                entry.DownloadedBytes = File.Exists(partialPath)
                    ? new FileInfo(partialPath).Length
                    : 0;
                entry.SetState("Paused");
            }

            await SaveAsync();
            return entry;
        }
        catch (Exception ex)
        {
            entry.DownloadedBytes = File.Exists(partialPath)
                ? new FileInfo(partialPath).Length
                : entry.DownloadedBytes;
            entry.SetState(ToDownloadError(ex));
            entry.IsComplete = false;
            await SaveAsync();
            throw;
        }
    }

    public void Pause(DownloadEntry entry)
    {
        if (!_downloadTokens.TryGetValue(entry.ItemId, out var cts))
            return;

        _stopReasons[entry.ItemId] = "pause";
        cts.Cancel();
    }

    public async Task ResumeAsync(DownloadEntry entry)
    {
        if (IsDownloading(entry.ItemId))
            return;

        var item = await App.Jellyfin.GetItemAsync(entry.ItemId);
        if (item is not null)
            await DownloadAsync(item, entry.QualityId);
    }

    public void Cancel(DownloadEntry entry)
    {
        if (_downloadTokens.TryGetValue(entry.ItemId, out var cts))
        {
            _stopReasons[entry.ItemId] = "cancel";
            cts.Cancel();
            return;
        }

        try
        {
            var partial = entry.LocalPath + ".part";
            if (File.Exists(partial))
                File.Delete(partial);
        }
        catch { }

        entry.Progress = 0;
        entry.DownloadedBytes = 0;
        entry.SetState("Cancelled");
        _ = SaveAsync();
    }


    private static bool IsQuickTransferActive(DownloadEntry entry)
        => entry.Status.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Queued", StringComparison.OrdinalIgnoreCase)
           || string.Equals(entry.Status, "Paused", StringComparison.OrdinalIgnoreCase);

    public async Task DismissFromQuickHistoryAsync(DownloadEntry entry)
    {
        if (IsQuickTransferActive(entry))
            return;

        entry.ShowInQuickHistory = false;
        await SaveAsync();
    }

    public async Task<int> ClearQuickHistoryAsync()
    {
        await LoadAsync();
        var rows = Items.Where(x => x.ShowInQuickHistory && !IsQuickTransferActive(x)).ToList();
        foreach (var row in rows)
            row.ShowInQuickHistory = false;

        if (rows.Count > 0)
            await SaveAsync();

        return rows.Count;
    }

    public async Task RemoveAsync(DownloadEntry entry)
    {
        if (IsDownloading(entry.ItemId))
            return;

        try
        {
            if (File.Exists(entry.LocalPath)) File.Delete(entry.LocalPath);
            var partialPath = entry.LocalPath + ".part";
            if (File.Exists(partialPath)) File.Delete(partialPath);
            var folder = Path.GetDirectoryName(entry.LocalPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) &&
                !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch { }

        Items.Remove(entry);
        await SaveAsync();
    }

    public async Task<int> RemoveAllInactiveAsync()
    {
        await LoadAsync();
        var rows = Items.Where(x => !IsDownloading(x.ItemId)).ToList();
        foreach (var row in rows)
            await RemoveAsync(row);
        return rows.Count;
    }

    public async Task<int> ClearFailedAndCancelledAsync()
    {
        await LoadAsync();
        var rows = Items.Where(x =>
            string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            x.Status.StartsWith("Download failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Status, "Ready to retry", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var row in rows)
            await RemoveAsync(row);
        return rows.Count;
    }

    private async Task SaveAsync()
    {
        await _manifestLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(MetadataRoot);
            var json = JsonSerializer.Serialize(Items.ToList(), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ManifestPath, json);
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private static async Task MoveCompletedDownloadAsync(string partialPath, string finalPath, CancellationToken ct)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Move(partialPath, finalPath);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                if (attempt == 4) break;
                await Task.Delay(150 * (attempt + 1), ct);
            }
        }

        throw lastError ?? new IOException("Could not finalize the downloaded file.");
    }

    private static string ToDownloadError(Exception ex)
    {
        if (ex is JellyfinApiException jellyfin)
        {
            var detail = string.IsNullOrWhiteSpace(jellyfin.Response)
                ? string.Empty
                : jellyfin.Response.Replace("\r", " ").Replace("\n", " ").Trim();

            if (jellyfin.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return "Download failed • Your Jellyfin session has expired. Sign in again and retry.";
            if (jellyfin.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return "Download failed • This Jellyfin user is not allowed to download this item.";

            if (detail.Length > 140)
                detail = detail[..140] + "…";

            return string.IsNullOrWhiteSpace(detail)
                ? $"Download failed • HTTP {(int)jellyfin.StatusCode}"
                : $"Download failed • HTTP {(int)jellyfin.StatusCode} • {detail}";
        }

        if (ex is HttpRequestException)
            return "Download failed • The server or network connection was interrupted. Retry when it is reachable.";

        if (ex is IOException &&
            ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
            return ex.Message.StartsWith("Download failed", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : $"Download failed • {ex.Message}";

        var message = ex.Message?.Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(message))
            return "Download failed";
        if (message.Length > 160)
            message = message[..160] + "…";
        return $"Download failed • {message}";
    }

    private static string GetExtension(BaseItemDto item)
    {
        var container = item.MediaSources?.FirstOrDefault()?.Container;
        if (string.IsNullOrWhiteSpace(container)) return ".media";
        var first = container.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return string.IsNullOrWhiteSpace(first) ? ".media" : "." + first;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit <= 1 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return string.Empty;
        if (bytesPerSecond >= 1024d * 1024d)
            return $"{bytesPerSecond / (1024d * 1024d):0.0} MB/s";
        return $"{bytesPerSecond / 1024d:0} KB/s";
    }

    private static string FormatEta(TimeSpan? eta)
    {
        if (eta is null || eta.Value < TimeSpan.Zero || eta.Value.TotalDays >= 1)
            return string.Empty;
        if (eta.Value.TotalHours >= 1)
            return $"ETA {eta.Value.Hours}h {eta.Value.Minutes}m";
        if (eta.Value.TotalMinutes >= 1)
            return $"ETA {eta.Value.Minutes}m {eta.Value.Seconds}s";
        return $"ETA {Math.Max(0, eta.Value.Seconds)}s";
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
