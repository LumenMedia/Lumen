using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Lumen.Models;

namespace Lumen.Views;

public sealed class PlaylistTrackRow
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
}

public sealed partial class MusicPlaylistPage : Page
{
    private string _playlistId = string.Empty;
    private readonly List<BaseItemDto> _tracks = [];
    private readonly List<PlaylistTrackRow> _rows = [];

    public MusicPlaylistPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string playlistId || string.IsNullOrWhiteSpace(playlistId))
            return;

        _playlistId = playlistId;
        await LoadPlaylistAsync();
    }

    private async Task LoadPlaylistAsync()
    {
        StatusText.Text = "Loading playlist…";
        try
        {
            var playlistTask = App.Jellyfin.GetItemAsync(_playlistId);
            var tracksTask = App.Jellyfin.GetMusicPlaylistTracksAsync(_playlistId, 2000);
            await Task.WhenAll(playlistTask, tracksTask);

            var playlist = playlistTask.Result;
            PlaylistTitle.Text = playlist?.Name ?? "Playlist";
            PlaylistBreadcrumbText.Text = PlaylistTitle.Text;
            PlaylistOverview.Text = playlist?.Overview ?? string.Empty;

            _tracks.Clear();
            _tracks.AddRange(tracksTask.Result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)));
            _rows.Clear();
            for (var i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                var runtime = track.RunTimeTicks is > 0 ? TimeSpan.FromTicks(track.RunTimeTicks.Value) : TimeSpan.Zero;
                _rows.Add(new PlaylistTrackRow
                {
                    Id = track.Id!,
                    Number = (i + 1).ToString(),
                    Title = track.Name ?? "Unknown track",
                    Artist = track.Artists?.FirstOrDefault() ?? track.AlbumArtist ?? "Unknown artist",
                    Album = track.Album ?? string.Empty,
                    Duration = runtime > TimeSpan.Zero
                        ? runtime.ToString(runtime.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss")
                        : string.Empty
                });
            }
            TracksList.ItemsSource = _rows;

            var totalTicks = _tracks.Sum(x => x.RunTimeTicks ?? 0);
            var total = TimeSpan.FromTicks(totalTicks);
            var meta = $"{_tracks.Count} song{(_tracks.Count == 1 ? "" : "s")}";
            if (total.TotalMinutes >= 1)
                meta += total.TotalHours >= 1
                    ? $" • {(int)total.TotalHours} hr {total.Minutes} min"
                    : $" • {Math.Max(1, (int)Math.Round(total.TotalMinutes))} min";
            PlaylistMeta.Text = meta;

            var imageTag = playlist?.ImageTags?.GetValueOrDefault("Primary") ?? playlist?.PrimaryImageTag;
            var imageUrl = App.Jellyfin.GetImageUrl(_playlistId, "Primary", imageTag, 720, 92);
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            {
                PlaylistArtwork.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(imageUri);
                PlaylistFallbackIcon.Visibility = Visibility.Collapsed;
            }

            StatusText.Text = _tracks.Count == 0 ? "This playlist does not contain any music tracks." : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load playlist: {ex.Message}";
        }
    }

    private async void Track_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PlaylistTrackRow row) return;
        var index = _tracks.FindIndex(x => string.Equals(x.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            await App.MusicPlayback.PlayQueueAsync(_tracks, index, shuffle: false);
    }

    private async void PlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count > 0)
            await App.MusicPlayback.PlayQueueAsync(_tracks, 0, shuffle: false);
    }

    private async void ShufflePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count > 0)
            await App.MusicPlayback.PlayQueueAsync(_tracks, 0, shuffle: true);
    }

    private void MusicBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
