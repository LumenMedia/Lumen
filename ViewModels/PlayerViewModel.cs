using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;
using Lumen.Services;

namespace Lumen.ViewModels;

public sealed class ChapterPreview
{
    public long StartPositionTicks { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string TimeText => TimeSpan.FromTicks(StartPositionTicks).TotalHours >= 1
        ? TimeSpan.FromTicks(StartPositionTicks).ToString(@"h\:mm\:ss")
        : TimeSpan.FromTicks(StartPositionTicks).ToString(@"m\:ss");
}

public sealed class PlayerStreamChoice
{
    public string Label { get; init; } = string.Empty;
    public int? Index { get; init; }
}

public partial class PlayerViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ItemId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StreamUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial long ResumePositionTicks { get; set; }
    [ObservableProperty]
    public partial int? SelectedAudioIndex { get; set; }
    [ObservableProperty]
    public partial int? SelectedSubtitleIndex { get; set; }
    [ObservableProperty]
    public partial string SelectedSubtitleLabel { get; set; } = "Off";
    [ObservableProperty]
    public partial string SelectedAudioLabel { get; set; } = "Default";
    [ObservableProperty]
    public partial string? PlaybackSessionId { get; set; }
    [ObservableProperty]
    public partial string CurrentPlayMethod { get; set; } = "DirectPlay";
    [ObservableProperty]
    public partial string PlaybackModeLabel { get; set; } = "Auto";
    [ObservableProperty]
    public partial string? ActiveMediaSourceId { get; set; }
    [ObservableProperty]
    public partial int SelectedAudioOrdinal { get; set; } = -1;
    [ObservableProperty]
    public partial bool IsAdaptiveStream { get; set; }
    [ObservableProperty]
    public partial long RunTimeTicks { get; set; }
    [ObservableProperty]
    public partial long StreamStartPositionTicks { get; set; }
    [ObservableProperty]
    public partial string SeriesId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SeriesName { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string MediaDetailsLabel { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string DiagnosticsText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsMode { get; set; } = "Auto";
    [ObservableProperty]
    public partial string StatsContainer { get; set; } = "Unknown";
    [ObservableProperty]
    public partial string StatsVideo { get; set; } = "Unknown";
    [ObservableProperty]
    public partial string StatsBitrate { get; set; } = "Unknown";
    [ObservableProperty]
    public partial string StatsAudio { get; set; } = "Unknown";
    [ObservableProperty]
    public partial string StatsSubtitle { get; set; } = "Off";
    [ObservableProperty]
    public partial string StatsReasons { get; set; } = "None";
    [ObservableProperty]
    public partial string NextEpisodeId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string NextEpisodeTitle { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasNextEpisode { get; set; }
    [ObservableProperty]
    public partial string ArtworkUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsOfflinePlayback { get; set; }
    [ObservableProperty]
    public partial string NextEpisodeImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string NextEpisodeOverview { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsServer { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsOutput { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsHdr { get; set; } = "SDR";
    [ObservableProperty]
    public partial string StatsFrameRate { get; set; } = "Unknown";
    [ObservableProperty]
    public partial string StatsTranscodeSpeed { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsTranscodeProgress { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatsHardware { get; set; } = string.Empty;

    private int? _currentSeasonNumber;
    private int? _currentEpisodeNumber;

    public ObservableCollection<PlayerStreamChoice> AudioChoices { get; } = [];
    public ObservableCollection<PlayerStreamChoice> SubtitleChoices { get; } = [];
    public ObservableCollection<ChapterPreview> Chapters { get; } = [];

    public async Task LoadAsync(PlaybackSelection selection, CancellationToken ct = default)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        AudioChoices.Clear();
        SubtitleChoices.Clear();
        Chapters.Clear();
        try
        {
            ItemId = selection.ItemId;
            ResumePositionTicks = Math.Max(0, selection.ResumePositionTicks);
            SelectedAudioIndex = selection.AudioStreamIndex;
            SelectedSubtitleIndex = selection.SubtitleStreamIndex;
            IsOfflinePlayback = selection.IsOffline && !string.IsNullOrWhiteSpace(selection.LocalFilePath);

            BaseItemDto? item = null;
            try
            {
                if (App.Jellyfin.IsAuthenticated)
                    item = await App.Jellyfin.GetItemAsync(selection.ItemId, ct);
            }
            catch { }

            if (item is null && IsOfflinePlayback)
            {
                item = new BaseItemDto
                {
                    Id = selection.ItemId,
                    Name = Path.GetFileNameWithoutExtension(selection.LocalFilePath),
                    Type = "Video"
                };
            }
            if (item is null) throw new InvalidOperationException("The media item could not be found.");

            // Fall back to Jellyfin UserData unless play-from-beginning was explicit.
            if (!selection.StartFromBeginning && ResumePositionTicks <= 0 && item.UserData?.Played != true)
            {
                var serverResumeTicks = Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0);
                if (serverResumeTicks > 0)
                {
                    ResumePositionTicks = serverResumeTicks;
                    selection.ResumePositionTicks = serverResumeTicks;
                }
            }
            else if (selection.StartFromBeginning)
            {
                ResumePositionTicks = 0;
                selection.ResumePositionTicks = 0;
            }

            Title = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesName)
                ? $"{item.SeriesName} — S{item.ParentIndexNumber:00}E{item.IndexNumber:00} {item.Name}"
                : item.Name ?? "Playback";
            SeriesId = item.SeriesId ?? string.Empty;
            SeriesName = item.SeriesName ?? string.Empty;
            _currentSeasonNumber = item.ParentIndexNumber;
            _currentEpisodeNumber = item.IndexNumber;
            RunTimeTicks = Math.Max(0, item.RunTimeTicks ?? 0);
            ArtworkUrl = !string.IsNullOrWhiteSpace(item.Id)
                ? App.Jellyfin.GetImageUrl(item.Id!, "Primary", item.PrimaryImageTag, 600, 88)
                : string.Empty;

            if (item.Chapters is { Count: > 0 })
            {
                for (var i = 0; i < item.Chapters.Count; i++)
                {
                    var chapter = item.Chapters[i];
                    Chapters.Add(new ChapterPreview
                    {
                        StartPositionTicks = Math.Max(0, chapter.StartPositionTicks),
                        Name = string.IsNullOrWhiteSpace(chapter.Name) ? $"Chapter {i + 1}" : chapter.Name!,
                        ImageUrl = !string.IsNullOrWhiteSpace(chapter.ImageTag)
                            ? App.Jellyfin.GetChapterImageUrl(item.Id!, i, chapter.ImageTag, 420, 88)
                            : string.Empty
                    });
                }
            }

            var videoInfo = (item.MediaStreams ?? []).FirstOrDefault(s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
            var detailBits = new List<string>();
            if (item.ProductionYear is > 0) detailBits.Add(item.ProductionYear.Value.ToString());
            if (videoInfo?.Height is > 0) detailBits.Add(videoInfo.Height >= 2160 ? "4K" : $"{videoInfo.Height}p");
            if (!string.IsNullOrWhiteSpace(videoInfo?.VideoRange) &&
                !string.Equals(videoInfo.VideoRange, "SDR", StringComparison.OrdinalIgnoreCase))
                detailBits.Add("HDR");
            if (!string.IsNullOrWhiteSpace(videoInfo?.Codec)) detailBits.Add(videoInfo.Codec!.ToUpperInvariant());
            if (videoInfo?.BitRate is > 0) detailBits.Add($"{videoInfo.BitRate.Value / 1_000_000d:0.0} Mbps");
            MediaDetailsLabel = string.Join(" • ", detailBits);

            foreach (var stream in (item.MediaStreams ?? []).Where(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase)))
            {
                var label = !string.IsNullOrWhiteSpace(stream.DisplayTitle) ? stream.DisplayTitle! : (stream.Language ?? $"Audio {stream.Index + 1}");
                if (stream.Channels is > 0) label += $" • {stream.Channels}ch";
                AudioChoices.Add(new PlayerStreamChoice { Label = label, Index = stream.Index });
            }
            if (SelectedAudioIndex is null)
            {
                var defaultAudio = (item.MediaStreams ?? []).FirstOrDefault(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase) && s.IsDefault);
                SelectedAudioIndex = defaultAudio?.Index ?? AudioChoices.FirstOrDefault()?.Index;
            }
            SelectedAudioLabel = AudioChoices.FirstOrDefault(x => x.Index == SelectedAudioIndex)?.Label ?? "Default";
            SelectedAudioOrdinal = FindAudioOrdinal(SelectedAudioIndex);

            SubtitleChoices.Add(new PlayerStreamChoice { Label = "Off", Index = null });
            foreach (var stream in (item.MediaStreams ?? []).Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)))
            {
                var label = !string.IsNullOrWhiteSpace(stream.DisplayTitle) ? stream.DisplayTitle! : (stream.Language ?? $"Subtitle {stream.Index + 1}");
                if (stream.IsForced) label += " • Forced";
                SubtitleChoices.Add(new PlayerStreamChoice { Label = label, Index = stream.Index });
            }
            SelectedSubtitleLabel = SubtitleChoices.FirstOrDefault(x => x.Index == SelectedSubtitleIndex)?.Label ?? "Off";

            if (IsOfflinePlayback)
            {
                StreamUrl = new Uri(Path.GetFullPath(selection.LocalFilePath!)).AbsoluteUri;
                CurrentPlayMethod = "Offline";
                PlaybackModeLabel = "Offline";
                StatsMode = "Offline";
                StatsServer = "Local file";
                StatsContainer = Path.GetExtension(selection.LocalFilePath!).TrimStart('.').ToUpperInvariant();
                IsAdaptiveStream = false;
                StreamStartPositionTicks = 0;
            }
            else
            {
                await NegotiateAsync(selection, ResumePositionTicks, ct);
                await RefreshNextEpisodeAsync(ct);
            }
        }
        catch (Exception ex) { ErrorMessage = ConnectionErrorMapper.ToFriendlyMessage(ex, App.MainViewModel.ServerUrl); }
        finally { IsBusy = false; }
    }

    public async Task RefreshNextEpisodeAsync(CancellationToken ct = default)
    {
        NextEpisodeId = string.Empty;
        NextEpisodeTitle = string.Empty;
        NextEpisodeOverview = string.Empty;
        NextEpisodeImageUrl = string.Empty;
        HasNextEpisode = false;

        if (string.IsNullOrWhiteSpace(SeriesId))
            return;

        try
        {
            BaseItemDto? episode = null;

            // Resolve episode order first; Jellyfin Next Up may lag until progress is saved.
            if (_currentSeasonNumber is int currentSeason &&
                _currentEpisodeNumber is int currentEpisode)
            {
                var episodes = await App.Jellyfin.GetItemsAsync(
                    parentId: SeriesId,
                    includeItemTypes: "Episode",
                    recursive: true,
                    sortBy: "ParentIndexNumber,IndexNumber,SortName",
                    sortOrder: "Ascending",
                    limit: 1000,
                    enableUserData: true,
                    fields: "Overview",
                    ct: ct);

                episode = episodes.Items
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Id) &&
                        !string.Equals(x.Id, ItemId, StringComparison.OrdinalIgnoreCase) &&
                        x.ParentIndexNumber is not null &&
                        x.IndexNumber is not null &&
                        (x.ParentIndexNumber.Value > currentSeason ||
                         (x.ParentIndexNumber.Value == currentSeason &&
                          x.IndexNumber.Value > currentEpisode)))
                    .OrderBy(x => x.ParentIndexNumber)
                    .ThenBy(x => x.IndexNumber)
                    .ThenBy(x => x.Name)
                    .FirstOrDefault();
            }

            if (episode is null)
            {
                var next = await App.Jellyfin.GetUpNextAsync(30, ct);
                episode = next.Items.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.Id) &&
                    !string.Equals(x.Id, ItemId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.SeriesId, SeriesId, StringComparison.OrdinalIgnoreCase));
            }

            if (episode is null)
                return;

            NextEpisodeId = episode.Id!;
            NextEpisodeTitle = episode.ParentIndexNumber is int season &&
                               episode.IndexNumber is int number
                ? $"S{season:00}E{number:00} • {episode.Name}"
                : episode.Name ?? "Next episode";
            NextEpisodeOverview = episode.Overview ?? string.Empty;

            var imageTag = episode.PrimaryImageTag;
            if (string.IsNullOrWhiteSpace(imageTag) &&
                episode.ImageTags is { } tags &&
                tags.TryGetValue("Primary", out var primaryTag))
            {
                imageTag = primaryTag;
            }

            NextEpisodeImageUrl = App.Jellyfin.GetImageUrl(
                episode.Id!,
                "Primary",
                imageTag,
                640,
                88);

            HasNextEpisode = true;
        }
        catch
        {
        }
    }

    public async Task NegotiateAsync(
        PlaybackSelection selection,
        long positionTicks,
        CancellationToken ct = default,
        bool enableDirectPlay = true,
        bool allowAudioStreamCopy = true)
    {
        var playback = await App.Jellyfin.GetPlaybackInfoAsync(
            ItemId,
            selection.MediaSourceId,
            SelectedAudioIndex,
            SelectedSubtitleIndex,
            Math.Max(0, positionTicks),
            enableDirectPlay,
            allowAudioStreamCopy,
            allowVideoStreamCopy: true,
            enableDirectStream: true,
            ct: ct);

        if (playback is null || playback.MediaSources.Count == 0)
            throw new InvalidOperationException("Jellyfin returned no playable media sources.");

        PlaybackSessionId = playback.PlaySessionId;
        var source = !string.IsNullOrWhiteSpace(selection.MediaSourceId)
            ? playback.MediaSources.FirstOrDefault(x => string.Equals(x.Id, selection.MediaSourceId, StringComparison.OrdinalIgnoreCase))
            : null;
        source ??= playback.MediaSources[0];

        // Transcode non-direct-play MKV video to avoid Media Foundation timestamp issues.
        var sourceContainer = source.Container ?? string.Empty;
        var needsSafeVideoTranscode =
            sourceContainer.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => string.Equals(x.Trim(), "mkv", StringComparison.OrdinalIgnoreCase)) &&
            !source.SupportsDirectPlay;

        if (needsSafeVideoTranscode)
        {
            var safePlayback = await App.Jellyfin.GetPlaybackInfoAsync(
                ItemId,
                source.Id ?? selection.MediaSourceId,
                SelectedAudioIndex,
                SelectedSubtitleIndex,
                Math.Max(0, positionTicks),
                enableDirectPlay: false,
                allowAudioStreamCopy: true,
                allowVideoStreamCopy: false,
                enableDirectStream: false,
                ct: ct);

            if (safePlayback is { MediaSources.Count: > 0 })
            {
                playback = safePlayback;
                PlaybackSessionId = safePlayback.PlaySessionId;
                source = !string.IsNullOrWhiteSpace(source.Id)
                    ? safePlayback.MediaSources.FirstOrDefault(x =>
                        string.Equals(x.Id, source.Id, StringComparison.OrdinalIgnoreCase))
                    : null;
                source ??= safePlayback.MediaSources[0];
            }
        }

        ActiveMediaSourceId = source.Id;
        selection.MediaSourceId = source.Id;
        selection.PlaySessionId = playback.PlaySessionId;

        var diagVideo = (source.MediaStreams ?? []).FirstOrDefault(x => string.Equals(x.Type, "Video", StringComparison.OrdinalIgnoreCase));
        var diagAudio = (source.MediaStreams ?? []).FirstOrDefault(x => string.Equals(x.Type, "Audio", StringComparison.OrdinalIgnoreCase) && x.Index == SelectedAudioIndex)
            ?? (source.MediaStreams ?? []).FirstOrDefault(x => string.Equals(x.Type, "Audio", StringComparison.OrdinalIgnoreCase));
        StatsMode = source.SupportsDirectPlay ? "Direct Play"
            : source.SupportsDirectStream ? "Direct Stream"
            : "Transcoding";
        StatsServer = App.MainViewModel.ServerName;
        StatsOutput = diagVideo?.Height is > 0
            ? $"{diagVideo.Width}×{diagVideo.Height}"
            : "Unknown";
        StatsHdr = !string.IsNullOrWhiteSpace(diagVideo?.VideoRange)
            ? diagVideo.VideoRange!
            : !string.IsNullOrWhiteSpace(diagVideo?.VideoRangeType) ? diagVideo.VideoRangeType! : "SDR";
        StatsFrameRate = diagVideo?.RealFrameRate is > 0
            ? $"{diagVideo.RealFrameRate:0.###} fps"
            : "Unknown";
        StatsHardware = StatsMode == "Transcoding" ? "Checking server…" : "Not required";
        StatsContainer = (source.Container ?? source.TranscodingContainer ?? "Unknown").ToUpperInvariant();
        StatsVideo = diagVideo is null
            ? "Unknown"
            : $"{diagVideo.Codec?.ToUpperInvariant() ?? "Unknown"}  •  {diagVideo.Width}×{diagVideo.Height}";
        StatsBitrate = diagVideo?.BitRate is > 0
            ? $"{diagVideo.BitRate.Value / 1_000_000d:0.0} Mbps"
            : "Unknown";
        StatsAudio = $"{diagAudio?.Codec?.ToUpperInvariant() ?? "Unknown"}" +
            (diagAudio?.Channels is > 0 ? $"  •  {diagAudio.Channels}ch" : string.Empty) +
            $"  •  {SelectedAudioLabel}";
        StatsSubtitle = SelectedSubtitleLabel;
        StatsReasons = source.TranscodingReasons is { Count: > 0 }
            ? string.Join(", ", source.TranscodingReasons)
            : "None";
        if (needsSafeVideoTranscode)
            StatsReasons = StatsReasons == "None"
                ? "Lumen safe MKV playback"
                : StatsReasons + ", Lumen safe MKV playback";

        DiagnosticsText =
            $"Mode: {StatsMode}\n" +
            $"Container: {StatsContainer}\n" +
            $"Video: {StatsVideo}\n" +
            $"Video bitrate: {StatsBitrate}\n" +
            $"Audio: {StatsAudio}\n" +
            $"Subtitle: {StatsSubtitle}\n" +
            $"Media source: {source.Id ?? "Unknown"}\n" +
            $"Transcoding reasons: {StatsReasons}";

        if (source.SupportsDirectPlay)
        {
            StreamStartPositionTicks = 0;
            CurrentPlayMethod = "DirectPlay";
            PlaybackModeLabel = "Direct Play";
            IsAdaptiveStream = false;
            StreamUrl = App.Jellyfin.GetDirectPlayUrl(ItemId, source.Id);
        }
        else if (!string.IsNullOrWhiteSpace(source.TranscodingUrl))
        {
            StreamStartPositionTicks = Math.Max(0, positionTicks);
            // Jellyfin's negotiated URL is authoritative.
            CurrentPlayMethod = source.SupportsDirectStream ? "DirectStream" : "Transcode";
            PlaybackModeLabel = source.SupportsDirectStream ? "Direct Stream" : "Transcoding";
            StreamUrl = EnsureStartTimeTicks(
                App.Jellyfin.GetNegotiatedUrl(source.TranscodingUrl),
                StreamStartPositionTicks);
            IsAdaptiveStream = string.Equals(source.TranscodingSubProtocol, "hls", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source.TranscodingSubProtocol, "dash", StringComparison.OrdinalIgnoreCase)
                || StreamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                || StreamUrl.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            throw new InvalidOperationException("This item is not directly playable and Jellyfin did not offer a transcoding stream.");
        }
    }


    private static string EnsureStartTimeTicks(string url, long startTimeTicks)
    {
        if (startTimeTicks <= 0) return url;

        var builder = new UriBuilder(url);
        var parts = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("StartTimeTicks=", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parts.Add($"StartTimeTicks={startTimeTicks}");
        builder.Query = string.Join("&", parts);
        return builder.Uri.ToString();
    }

    public void SetSelections(int? audioIndex, int? subtitleIndex)
    {
        SelectedAudioIndex = audioIndex;
        SelectedSubtitleIndex = subtitleIndex;
        SelectedAudioLabel = AudioChoices.FirstOrDefault(x => x.Index == audioIndex)?.Label ?? "Default";
        SelectedAudioOrdinal = FindAudioOrdinal(audioIndex);
        SelectedSubtitleLabel = SubtitleChoices.FirstOrDefault(x => x.Index == subtitleIndex)?.Label ?? "Off";
    }

    private int FindAudioOrdinal(int? jellyfinStreamIndex)
    {
        if (jellyfinStreamIndex is null) return -1;
        for (var i = 0; i < AudioChoices.Count; i++)
            if (AudioChoices[i].Index == jellyfinStreamIndex) return i;
        return -1;
    }
}
