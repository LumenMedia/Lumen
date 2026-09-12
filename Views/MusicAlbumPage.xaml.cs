using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Lumen.Models;

namespace Lumen.Views;

public sealed class AlbumTrackRow
{
    public string Id { get; set; } = string.Empty;
    public string TrackNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string DiscLabel { get; set; } = string.Empty;
}

public sealed partial class MusicAlbumPage : Page
{
    private string _albumId = string.Empty;
    private string _artistId = string.Empty;
    private readonly List<AlbumTrackRow> _tracks = [];
    private readonly List<BaseItemDto> _trackItems = [];

    public MusicAlbumPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string albumId || string.IsNullOrWhiteSpace(albumId))
            return;

        _albumId = albumId;
        await LoadAlbumAsync(albumId);
    }

    private async Task LoadAlbumAsync(string albumId)
    {
        StatusText.Text = "Loading album…";
        try
        {
            var album = await App.Jellyfin.GetItemAsync(albumId);
            var result = await App.Jellyfin.GetAlbumTracksAsync(albumId);

            AlbumTitle.Text = album?.Name ?? "Album";
            AlbumBreadcrumbText.Text = AlbumTitle.Text;
            AlbumArtist.Text = album?.AlbumArtist ?? album?.Artists?.FirstOrDefault() ?? string.Empty;
            _artistId = album?.AlbumArtists?.FirstOrDefault()?.Id
                ?? album?.ArtistItems?.FirstOrDefault()?.Id
                ?? string.Empty;
            AlbumArtistButton.IsEnabled = !string.IsNullOrWhiteSpace(_artistId);

            var year = album?.ProductionYear?.ToString();
            var durationTicks = result.Items.Sum(x => x.RunTimeTicks ?? 0);
            var duration = TimeSpan.FromTicks(durationTicks);
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(year)) meta.Add(year);
            meta.Add($"{result.Items.Count} song{(result.Items.Count == 1 ? "" : "s")}");
            if (duration.TotalMinutes >= 1) meta.Add(duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours} hr {duration.Minutes} min"
                : $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} min");
            AlbumMeta.Text = string.Join(" • ", meta);

            var tag = album?.ImageTags?.GetValueOrDefault("Primary") ?? album?.PrimaryImageTag;
            var imageUrl = App.Jellyfin.GetImageUrl(albumId, "Primary", tag, 720, 92);
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
                AlbumArtwork.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(imageUri);

            _tracks.Clear();
            _trackItems.Clear();
            foreach (var track in result.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .OrderBy(x => x.ParentIndexNumber ?? 1)
                .ThenBy(x => x.IndexNumber ?? int.MaxValue)
                .ThenBy(x => x.Name))
            {
                var runtime = track.RunTimeTicks is > 0 ? TimeSpan.FromTicks(track.RunTimeTicks.Value) : TimeSpan.Zero;
                _trackItems.Add(track);
                _tracks.Add(new AlbumTrackRow
                {
                    Id = track.Id!,
                    TrackNumber = (track.IndexNumber ?? (_tracks.Count + 1)).ToString(),
                    Title = track.Name ?? "Unknown track",
                    Artist = track.Artists?.FirstOrDefault() ?? track.AlbumArtist ?? AlbumArtist.Text,
                    Duration = runtime > TimeSpan.Zero ? runtime.ToString(runtime.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") : string.Empty,
                    DiscLabel = (track.ParentIndexNumber ?? 1) > 1 ? $"Disc {track.ParentIndexNumber}" : string.Empty
                });
            }

            TracksList.ItemsSource = _tracks;
            StatusText.Text = _tracks.Count == 0 ? "No tracks were returned for this album." : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load album: {ex.Message}";
        }
    }

    private async void Track_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AlbumTrackRow track || string.IsNullOrWhiteSpace(track.Id))
            return;

        var index = _trackItems.FindIndex(x =>
            string.Equals(x.Id, track.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        await App.MusicPlayback.PlayQueueAsync(_trackItems, index, shuffle: false);
    }

    private async void PlayAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (_trackItems.Count == 0) return;
        await App.MusicPlayback.PlayQueueAsync(_trackItems, 0, shuffle: false);
    }

    private async void ShuffleAlbum_Click(object sender, RoutedEventArgs e)
    {
        if (_trackItems.Count == 0) return;
        await App.MusicPlayback.PlayQueueAsync(_trackItems, 0, shuffle: true);
    }

    private void AlbumArtist_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_artistId))
            Frame.Navigate(typeof(ArtistPage), _artistId);
    }

    private void MusicBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
