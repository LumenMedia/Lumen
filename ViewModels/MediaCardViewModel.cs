using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;

namespace Lumen.ViewModels;

public partial class MediaCardViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Overview { get; set; } = string.Empty;
    [ObservableProperty]
    public partial int? Year { get; set; }
    [ObservableProperty]
    public partial double? Rating { get; set; }
    [ObservableProperty]
    public partial string RatingText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string BackdropUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool Played { get; set; }
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }
    [ObservableProperty]
    public partial double Progress { get; set; }
    [ObservableProperty]
    public partial string Genres { get; set; } = string.Empty;
    [ObservableProperty]
    public partial int ResolutionHeight { get; set; }
    [ObservableProperty]
    public partial DateTime? DateCreated { get; set; }
    [ObservableProperty]
    public partial long RuntimeTicks { get; set; }
    [ObservableProperty]
    public partial long ResumePositionTicks { get; set; }
    [ObservableProperty]
    public partial string RemainingText { get; set; } = string.Empty;

    public bool HasProgress => Progress > 0.1 && Progress < 99.9;

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(HasProgress));

    public static MediaCardViewModel FromDto(BaseItemDto item) => new()
    {
        Id = item.Id ?? string.Empty,
        Name = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesName)
            ? item.SeriesName!
            : item.Name ?? string.Empty,
        Type = item.Type ?? string.Empty,
        Subtitle = BuildSubtitle(item),
        Overview = item.Overview ?? string.Empty,
        Year = item.ProductionYear ?? item.PremiereDate?.Year,
        Rating = item.CommunityRating,
        RatingText = item.CommunityRating is > 0 ? item.CommunityRating.Value.ToString("0.0") : string.Empty,
        Played = item.UserData?.Played ?? false,
        IsFavorite = item.UserData?.IsFavorite ?? false,
        Progress = CalculateProgress(item),
        Genres = item.Genres is null ? string.Empty : string.Join("|", item.Genres),
        ResolutionHeight = (item.MediaStreams ?? []).FirstOrDefault(x => string.Equals(x.Type, "Video", StringComparison.OrdinalIgnoreCase))?.Height ?? 0,
        DateCreated = item.DateCreated,
        RuntimeTicks = item.RunTimeTicks ?? 0,
        ResumePositionTicks = Math.Max(0, item.UserData?.PlaybackPositionTicks ?? 0),
        RemainingText = BuildRemainingText(item)
    };

    private static string BuildSubtitle(BaseItemDto item)
    {
        var bits = new List<string>();
        if (item.ProductionYear is not null) bits.Add(item.ProductionYear.Value.ToString());
        if (item.Type == "Episode")
        {
            var episode = new List<string>();
            if (item.ParentIndexNumber is not null) episode.Add($"S{item.ParentIndexNumber.Value:00}");
            if (item.IndexNumber is not null) episode.Add($"E{item.IndexNumber.Value:00}");
            if (episode.Count > 0) bits.Add(string.Join("", episode));
            else if (!string.IsNullOrWhiteSpace(item.Name)) bits.Add(item.Name!);
        }
        else if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(item.AlbumArtist)) bits.Add(item.AlbumArtist!);
            else if (item.Artists?.FirstOrDefault() is string albumArtist && !string.IsNullOrWhiteSpace(albumArtist)) bits.Add(albumArtist);
            if (item.ProductionYear is not null && !bits.Contains(item.ProductionYear.Value.ToString())) bits.Add(item.ProductionYear.Value.ToString());
        }
        else if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
        {
            if (item.Artists?.FirstOrDefault() is string artist && !string.IsNullOrWhiteSpace(artist)) bits.Add(artist);
            if (!string.IsNullOrWhiteSpace(item.Album)) bits.Add(item.Album!);
        }
        else if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
        {
            bits.Add("Artist");
        }
        else if (!string.IsNullOrWhiteSpace(item.Type))
        {
            bits.Add(item.Type!);
        }
        return string.Join(" • ", bits);
    }

    private static string BuildRemainingText(BaseItemDto item)
    {
        var runtime = item.RunTimeTicks ?? 0;
        var position = item.UserData?.PlaybackPositionTicks ?? 0;
        if (runtime <= 0 || position <= 0 || position >= runtime) return string.Empty;
        var remaining = TimeSpan.FromTicks(Math.Max(0, runtime - position));
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m left";
        return $"{Math.Max(1, remaining.Minutes)} min left";
    }

    private static double CalculateProgress(BaseItemDto item)
    {
        if (item.RunTimeTicks is not > 0 || item.UserData?.PlaybackPositionTicks is not > 0) return 0;
        return Math.Clamp(item.UserData.PlaybackPositionTicks / (double)item.RunTimeTicks.Value * 100d, 0d, 100d);
    }
}
