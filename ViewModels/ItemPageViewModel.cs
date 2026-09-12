using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;

namespace Lumen.ViewModels;

public sealed class CastMemberViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
}

public sealed class PlaybackChoice
{
    public string Label { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public int? AudioStreamIndex { get; init; }
    public int? SubtitleStreamIndex { get; init; }
}

public sealed class SeasonChoice
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

public sealed class GenreChoice
{
    public string Name { get; init; } = string.Empty;
    public override string ToString() => Name;
}

public partial class ItemPageViewModel : ObservableObject
{
    private long _resumePositionTicks;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ItemType { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsEpisode { get; set; }
    [ObservableProperty]
    public partial string BreadcrumbText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SeriesBreadcrumbText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SeasonBreadcrumbText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string EpisodeBreadcrumbText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SeasonId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SeriesId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string PosterUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string LogoUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasLogo { get; set; }
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Genres { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Overview { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string TechnicalInfo { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Tagline { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasCast { get; set; }
    [ObservableProperty]
    public partial string SelectedAudioLabel { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SelectedSubtitleLabel { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SelectedQualityLabel { get; set; } = "Auto • Recommended";
    [ObservableProperty]
    public partial PlaybackChoice? SelectedAudioChoice { get; set; }
    [ObservableProperty]
    public partial PlaybackChoice? SelectedSubtitleChoice { get; set; }
    [ObservableProperty]
    public partial PlaybackChoice? SelectedQualityChoice { get; set; }
    [ObservableProperty]
    public partial bool HasResume { get; set; }
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }
    [ObservableProperty]
    public partial bool IsPlayed { get; set; }
    [ObservableProperty]
    public partial bool IsSeries { get; set; }
    [ObservableProperty]
    public partial bool IsCollection { get; set; }
    [ObservableProperty]
    public partial bool HasExtras { get; set; }
    [ObservableProperty]
    public partial bool HasCollectionItems { get; set; }
    [ObservableProperty]
    public partial bool IsPlayable { get; set; } = true;
    [ObservableProperty]
    public partial bool HasEpisodes { get; set; }
    [ObservableProperty]
    public partial bool HasSimilar { get; set; }
    [ObservableProperty]
    public partial SeasonChoice? SelectedSeason { get; set; }

    public ObservableCollection<CastMemberViewModel> Cast { get; } = [];
    public ObservableCollection<PlaybackChoice> AudioChoices { get; } = [];
    public ObservableCollection<PlaybackChoice> SubtitleChoices { get; } = [];
    public ObservableCollection<PlaybackChoice> QualityChoices { get; } = [];
    public ObservableCollection<SeasonChoice> Seasons { get; } = [];
    public ObservableCollection<MediaCardViewModel> Episodes { get; } = [];
    public ObservableCollection<MediaCardViewModel> SimilarItems { get; } = [];
    public ObservableCollection<MediaCardViewModel> Extras { get; } = [];
    public ObservableCollection<MediaCardViewModel> CollectionItems { get; } = [];
    public ObservableCollection<GenreChoice> GenreChoices { get; } = [];

    public string FavoriteActionText => IsFavorite ? "Remove favourite" : "Favourite";
    public string PlayedActionText => IsPlayed ? "Mark unplayed" : "Mark played";

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteActionText));
    partial void OnIsPlayedChanged(bool value) => OnPropertyChanged(nameof(PlayedActionText));

    public PlaybackChoice? SelectedAudio => SelectedAudioChoice ?? AudioChoices.FirstOrDefault(x => x.Label == SelectedAudioLabel);
    public PlaybackChoice? SelectedSubtitle => SelectedSubtitleChoice ?? SubtitleChoices.FirstOrDefault(x => x.Label == SelectedSubtitleLabel);
    public PlaybackChoice? SelectedQuality => SelectedQualityChoice ?? QualityChoices.FirstOrDefault(x => x.Label == SelectedQualityLabel);

    partial void OnSelectedAudioChoiceChanged(PlaybackChoice? value)
    {
        if (value is not null) SelectedAudioLabel = value.Label;
    }

    partial void OnSelectedSubtitleChoiceChanged(PlaybackChoice? value)
    {
        if (value is not null) SelectedSubtitleLabel = value.Label;
    }

    partial void OnSelectedQualityChoiceChanged(PlaybackChoice? value)
    {
        if (value is not null) SelectedQualityLabel = value.Label;
    }

    public async Task LoadAsync(string itemId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Cast.Clear();
            AudioChoices.Clear();
            SubtitleChoices.Clear();
            QualityChoices.Clear();
            SelectedAudioChoice = null;
            SelectedSubtitleChoice = null;
            SelectedQualityChoice = null;
            Seasons.Clear();
            Episodes.Clear();
            SimilarItems.Clear();
            Extras.Clear();
            CollectionItems.Clear();
            GenreChoices.Clear();

            var item = await App.Jellyfin.GetItemAsync(itemId, ct);
            if (item is null) return;

            Id = item.Id ?? string.Empty;
            ItemType = item.Type ?? string.Empty;
            IsSeries = string.Equals(ItemType, "Series", StringComparison.OrdinalIgnoreCase);
            IsEpisode = string.Equals(ItemType, "Episode", StringComparison.OrdinalIgnoreCase);
            SeriesId = item.SeriesId ?? string.Empty;
            SeriesBreadcrumbText = IsEpisode ? (item.SeriesName ?? "Series") : string.Empty;
            SeasonBreadcrumbText = IsEpisode
                ? (item.ParentIndexNumber is int seasonNumber ? $"Season {seasonNumber}" : "Season")
                : string.Empty;
            EpisodeBreadcrumbText = IsEpisode
                ? (item.IndexNumber is int episodeNumber ? $"Episode {episodeNumber}" : "Episode")
                : string.Empty;
            SeasonId = IsEpisode ? (item.ParentId ?? string.Empty) : string.Empty;
            BreadcrumbText = IsEpisode
                ? $"{SeriesBreadcrumbText} › {SeasonBreadcrumbText} › {EpisodeBreadcrumbText}"
                : string.Empty;
            IsCollection = string.Equals(ItemType, "BoxSet", StringComparison.OrdinalIgnoreCase);
            IsPlayable = !IsSeries && !IsCollection;
            Name = item.Name ?? string.Empty;
            Overview = item.Overview ?? string.Empty;
            Tagline = item.Taglines?.FirstOrDefault() ?? string.Empty;
            IsFavorite = item.UserData?.IsFavorite ?? false;
            IsPlayed = item.UserData?.Played ?? false;

            _resumePositionTicks = IsPlayed ? 0 : Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0);
            HasResume = _resumePositionTicks > TimeSpan.FromSeconds(10).Ticks;

            var parts = new List<string>();
            if (item.ProductionYear is not null) parts.Add(item.ProductionYear.Value.ToString());
            if (!string.IsNullOrWhiteSpace(item.OfficialRating)) parts.Add(item.OfficialRating!);
            if (!string.IsNullOrWhiteSpace(item.Type)) parts.Add(item.Type!);
            if (item.RunTimeTicks is > 0) parts.Add(FormatRuntime(item.RunTimeTicks.Value));
            if (item.CommunityRating is not null) parts.Add($"★ {item.CommunityRating:0.0}");
            Subtitle = string.Join(" • ", parts);
            Genres = item.Genres is null ? string.Empty : string.Join(" • ", item.Genres.Where(g => !string.IsNullOrWhiteSpace(g)));
            foreach (var genre in (item.Genres ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase))
                GenreChoices.Add(new GenreChoice { Name = genre });

            var backdropTag = item.ImageTags?.GetValueOrDefault("Backdrop") ?? item.BackdropImageTags?.FirstOrDefault();
            var primaryTag = item.ImageTags?.GetValueOrDefault("Primary") ?? item.PrimaryImageTag;

            // Episode stills belong in the wide hero area.
            if (IsEpisode)
            {
                ImageUrl = !string.IsNullOrWhiteSpace(primaryTag)
                    ? App.Jellyfin.GetImageUrl(Id, "Primary", primaryTag, 1600, 88)
                    : !string.IsNullOrWhiteSpace(backdropTag)
                        ? App.Jellyfin.GetImageUrl(Id, "Backdrop", backdropTag, 1600, 88)
                        : string.Empty;
                PosterUrl = !string.IsNullOrWhiteSpace(primaryTag)
                    ? App.Jellyfin.GetImageUrl(Id, "Primary", primaryTag, 640, 90)
                    : string.Empty;
            }
            else
            {
                ImageUrl = App.Jellyfin.GetImageUrl(Id, "Backdrop", backdropTag, 1600, 88);
                PosterUrl = App.Jellyfin.GetImageUrl(Id, "Primary", primaryTag, 640, 90);
            }

            // Prefer the series poster for episode portrait artwork.
            if (IsEpisode && !string.IsNullOrWhiteSpace(SeriesId))
            {
                try
                {
                    var series = await App.Jellyfin.GetItemAsync(SeriesId, ct);
                    if (series is not null)
                    {
                        var seriesPrimaryTag = series.ImageTags?.GetValueOrDefault("Primary") ?? series.PrimaryImageTag;
                        if (!string.IsNullOrWhiteSpace(seriesPrimaryTag))
                            PosterUrl = App.Jellyfin.GetImageUrl(SeriesId, "Primary", seriesPrimaryTag, 640, 90);

                        if (string.IsNullOrWhiteSpace(ImageUrl))
                        {
                            var seriesBackdropTag = series.ImageTags?.GetValueOrDefault("Backdrop") ?? series.BackdropImageTags?.FirstOrDefault();
                            ImageUrl = App.Jellyfin.GetImageUrl(SeriesId, "Backdrop", seriesBackdropTag, 1600, 88);
                        }
                    }
                }
                catch { }
            }

            var logoTag = item.ImageTags?.GetValueOrDefault("Logo");
            HasLogo = !string.IsNullOrWhiteSpace(logoTag);
            LogoUrl = HasLogo ? App.Jellyfin.GetImageUrl(Id, "Logo", logoTag, 720, 90) : string.Empty;

            foreach (var person in (item.People ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Take(16))
            {
                Cast.Add(new CastMemberViewModel
                {
                    Id = person.Id ?? string.Empty,
                    Name = person.Name!,
                    Role = person.Role ?? person.Type ?? "Cast",
                    ImageUrl = !string.IsNullOrWhiteSpace(person.Id)
                        ? App.Jellyfin.GetImageUrl(person.Id!, "Primary", person.PrimaryImageTag, 180, 88)
                        : string.Empty
                });
            }
            HasCast = Cast.Count > 0;

            foreach (var stream in (item.MediaStreams ?? []).Where(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase)))
            {
                var label = !string.IsNullOrWhiteSpace(stream.DisplayTitle)
                    ? stream.DisplayTitle!
                    : string.IsNullOrWhiteSpace(stream.Language) ? $"Audio {stream.Index + 1}" : stream.Language!;
                if (stream.Channels is not null) label += $" • {stream.Channels}ch";
                AudioChoices.Add(new PlaybackChoice { Label = label, AudioStreamIndex = stream.Index });
            }
            if (AudioChoices.Count == 0) AudioChoices.Add(new PlaybackChoice { Label = "Default audio" });
            SelectedAudioChoice = AudioChoices.FirstOrDefault();
            SelectedAudioLabel = SelectedAudioChoice?.Label ?? string.Empty;

            SubtitleChoices.Add(new PlaybackChoice { Label = "Off", SubtitleStreamIndex = null });
            foreach (var stream in (item.MediaStreams ?? []).Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)))
            {
                var label = !string.IsNullOrWhiteSpace(stream.DisplayTitle)
                    ? stream.DisplayTitle!
                    : string.IsNullOrWhiteSpace(stream.Language) ? $"Subtitle {stream.Index + 1}" : stream.Language!;
                if (stream.IsForced) label += " • Forced";
                SubtitleChoices.Add(new PlaybackChoice { Label = label, SubtitleStreamIndex = stream.Index });
            }
            SelectedSubtitleChoice = SubtitleChoices.FirstOrDefault();
            SelectedSubtitleLabel = SelectedSubtitleChoice?.Label ?? "Off";

            if (IsPlayable)
            {
                try
                {
                    var playback = await App.Jellyfin.GetPlaybackInfoAsync(Id, ct: ct);
                    foreach (var source in playback?.MediaSources ?? [])
                    {
                        var stream = (source.MediaStreams ?? []).FirstOrDefault(s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
                        var quality = BuildQualityLabel(stream, source);
                        if (!string.IsNullOrWhiteSpace(quality))
                            QualityChoices.Add(new PlaybackChoice { Label = quality, SourceId = source.Id });
                    }
                }
                catch { }

                if (QualityChoices.Count == 0)
                {
                    var video = (item.MediaStreams ?? []).FirstOrDefault(s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
                    var fallbackLabel = BuildQualityLabel(video, new MediaSourceInfoDto());
                    QualityChoices.Add(new PlaybackChoice { Label = string.IsNullOrWhiteSpace(fallbackLabel) ? "Source quality" : fallbackLabel });
                }

                QualityChoices.Insert(0, new PlaybackChoice { Label = "Auto • Recommended" });
                SelectedQualityChoice = QualityChoices[0];
                SelectedQualityLabel = SelectedQualityChoice.Label;
            }

            var videoStream = (item.MediaStreams ?? []).FirstOrDefault(s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
            var tech = new List<string>();
            if (videoStream?.Width is > 0 && videoStream.Height is > 0) tech.Add($"{videoStream.Width}×{videoStream.Height}");
            if (!string.IsNullOrWhiteSpace(videoStream?.Codec)) tech.Add(videoStream!.Codec!.ToUpperInvariant());
            if (videoStream?.BitRate is > 0) tech.Add($"{videoStream.BitRate.Value / 1_000_000d:0.0} Mbps");
            if (item.RunTimeTicks is > 0) tech.Add(FormatRuntime(item.RunTimeTicks.Value));
            TechnicalInfo = string.Join(" • ", tech);

            if (IsEpisode && !string.IsNullOrWhiteSpace(item.ParentId))
            {
                try { await LoadSeasonAsync(item.ParentId!, ct); } catch { }
            }

            if (IsSeries)
            {
                try
                {
                    var seasons = await App.Jellyfin.GetSeasonsAsync(Id, ct);
                    foreach (var season in seasons.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                        Seasons.Add(new SeasonChoice { Id = season.Id!, Name = season.Name ?? $"Season {season.IndexNumber}" });

                    SelectedSeason = Seasons.FirstOrDefault();
                    if (SelectedSeason is not null)
                        await LoadSeasonAsync(SelectedSeason.Id, ct);
                }
                catch { }
            }

            if (IsCollection)
            {
                try
                {
                    var collection = await App.Jellyfin.GetCollectionItemsAsync(Id, ct);
                    foreach (var entry in collection.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                    {
                        var card = MediaCardViewModel.FromDto(entry);
                        card.ImageUrl = App.Jellyfin.GetImageUrl(entry.Id!, "Primary",
                            entry.ImageTags?.GetValueOrDefault("Primary") ?? entry.PrimaryImageTag, 360, 88);
                        CollectionItems.Add(card);
                    }
                    HasCollectionItems = CollectionItems.Count > 0;
                }
                catch { HasCollectionItems = false; }
            }

            if (!IsSeries && !IsCollection)
            {
                try
                {
                    var extras = await App.Jellyfin.GetSpecialFeaturesAsync(Id, ct);
                    foreach (var entry in extras.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                    {
                        var card = MediaCardViewModel.FromDto(entry);
                        card.ImageUrl = App.Jellyfin.GetImageUrl(entry.Id!, "Primary",
                            entry.ImageTags?.GetValueOrDefault("Primary") ?? entry.PrimaryImageTag, 360, 88);
                        Extras.Add(card);
                    }
                    HasExtras = Extras.Count > 0;
                }
                catch { HasExtras = false; }
            }

            try
            {
                var similar = await App.Jellyfin.GetSimilarItemsAsync(Id, 12, ct);
                foreach (var entry in similar.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                {
                    var card = MediaCardViewModel.FromDto(entry);
                    card.ImageUrl = App.Jellyfin.GetImageUrl(entry.Id!, "Primary", entry.PrimaryImageTag, 360, 88);
                    SimilarItems.Add(card);
                }
                HasSimilar = SimilarItems.Count > 0;
            }
            catch
            {
                HasSimilar = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadSelectedSeasonAsync(CancellationToken ct = default)
    {
        if (SelectedSeason is null) return;
        await LoadSeasonAsync(SelectedSeason.Id, ct);
    }
    public async Task SelectSeasonAsync(string seasonId, CancellationToken ct = default)
    {
        var season = Seasons.FirstOrDefault(x => string.Equals(x.Id, seasonId, StringComparison.OrdinalIgnoreCase));
        if (season is null) return;
        SelectedSeason = season;
        await LoadSeasonAsync(season.Id, ct);
    }

    private async Task LoadSeasonAsync(string seasonId, CancellationToken ct)
    {
        Episodes.Clear();
        var result = await App.Jellyfin.GetEpisodesAsync(seasonId, ct);
        foreach (var ep in result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
        {
            var card = MediaCardViewModel.FromDto(ep);
            card.Name = ep.Name ?? card.Name;
            var episodeTag = ep.ImageTags?.GetValueOrDefault("Primary") ?? ep.PrimaryImageTag;
            card.ImageUrl = !string.IsNullOrWhiteSpace(episodeTag)
                ? App.Jellyfin.GetImageUrlFit(ep.Id!, "Primary", episodeTag, 640, 90)
                : !string.IsNullOrWhiteSpace(ep.SeriesId)
                    ? App.Jellyfin.GetImageUrlFit(ep.SeriesId!, "Backdrop", ep.BackdropImageTags?.FirstOrDefault(), 640, 90)
                    : string.Empty;
            Episodes.Add(card);
        }
        HasEpisodes = Episodes.Count > 0;
    }

    public async Task ToggleFavoriteAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return;
        var next = !IsFavorite;
        await App.Jellyfin.MarkFavoriteAsync(Id, next);
        IsFavorite = next;
    }

    public async Task TogglePlayedAsync()
    {
        if (string.IsNullOrWhiteSpace(Id)) return;
        var next = !IsPlayed;
        await App.Jellyfin.MarkPlayedAsync(Id, next);
        IsPlayed = next;
        if (next)
        {
            _resumePositionTicks = 0;
            HasResume = false;
        }
    }

    public PlaybackSelection BuildPlaybackSelection(bool startFromBeginning = false)
    {
        var audio = SelectedAudio;
        var subtitle = SelectedSubtitle;
        var quality = SelectedQuality;
        var selection = new PlaybackSelection
        {
            ItemId = Id,
            MediaSourceId = quality?.SourceId,
            AudioStreamIndex = audio?.AudioStreamIndex,
            SubtitleStreamIndex = subtitle?.SubtitleStreamIndex,
            ResumePositionTicks = startFromBeginning ? 0 : _resumePositionTicks,
            StartFromBeginning = startFromBeginning,
            PlayMethod = "DirectStream"
        };

        if (App.Settings.PreferDownloadedPlayback)
        {
            var local = App.Downloads.GetCompletedDownload(Id);
            if (local is not null)
            {
                selection.LocalFilePath = local.LocalPath;
                selection.IsOffline = true;
            }
        }

        return selection;
    }

    private static string BuildQualityLabel(MediaStreamDto? stream, MediaSourceInfoDto source)
    {
        var parts = new List<string>();
        if (stream?.Height is > 0)
        {
            var label = stream.Height >= 2160 ? "4K" : stream.Height >= 1440 ? "1440p" : stream.Height >= 1080 ? "1080p" : stream.Height >= 720 ? "720p" : $"{stream.Height}p";
            parts.Add(label);
        }
        if (!string.IsNullOrWhiteSpace(stream?.Codec)) parts.Add(stream!.Codec!.ToUpperInvariant());
        if (source.Bitrate is > 0) parts.Add($"{source.Bitrate.Value / 1_000_000d:0.0} Mbps");
        if (source.SupportsDirectPlay) parts.Add("Direct Play");
        return string.Join(" • ", parts);
    }

    private static string FormatRuntime(long ticks)
    {
        var minutes = TimeSpan.FromTicks(ticks).TotalMinutes;
        var total = (int)Math.Round(minutes);
        return total >= 60 ? $"{total / 60}h {total % 60}m" : $"{total}m";
    }
}
