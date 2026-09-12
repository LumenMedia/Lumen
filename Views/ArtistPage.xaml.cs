using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Lumen.Models;
using Lumen.ViewModels;

namespace Lumen.Views;

public sealed class ArtistSongRow
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
}

public sealed partial class ArtistPage : Page
{
    private string _artistId = string.Empty;
    private readonly List<BaseItemDto> _songs = [];
    private readonly List<ArtistSongRow> _songRows = [];

    public ArtistPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string artistId || string.IsNullOrWhiteSpace(artistId))
            return;

        _artistId = artistId;
        await LoadArtistAsync();
    }

    private async Task LoadArtistAsync()
    {
        StatusText.Text = "Loading artist…";
        try
        {
            var artistTask = App.Jellyfin.GetItemAsync(_artistId);
            var albumsTask = App.Jellyfin.GetArtistAlbumsAsync(_artistId);
            var songsTask = App.Jellyfin.GetArtistSongsAsync(_artistId);
            await Task.WhenAll(artistTask, albumsTask, songsTask);

            var artist = artistTask.Result;
            ArtistTitle.Text = artist?.Name ?? "Artist";
            ArtistBreadcrumbText.Text = ArtistTitle.Text;
            ArtistOverview.Text = artist?.Overview ?? string.Empty;

            var imageTag = artist?.ImageTags?.GetValueOrDefault("Primary") ?? artist?.PrimaryImageTag;
            var imageUrl = App.Jellyfin.GetImageUrl(_artistId, "Primary", imageTag, 640, 92);
            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
                ArtistArtwork.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(imageUri);

            var albumCards = albumsTask.Result.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Select(x =>
                {
                    var card = MediaCardViewModel.FromDto(x);
                    card.ImageUrl = App.Jellyfin.GetImageUrl(
                        x.Id!,
                        "Primary",
                        x.ImageTags?.GetValueOrDefault("Primary") ?? x.PrimaryImageTag,
                        420,
                        90);
                    return card;
                })
                .ToList();
            AlbumsGrid.ItemsSource = albumCards;

            _songs.Clear();
            _songs.AddRange(songsTask.Result.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .OrderBy(x => x.Album)
                .ThenBy(x => x.ParentIndexNumber ?? 1)
                .ThenBy(x => x.IndexNumber ?? int.MaxValue)
                .ThenBy(x => x.Name));

            _songRows.Clear();
            for (var i = 0; i < _songs.Count; i++)
            {
                var song = _songs[i];
                var duration = song.RunTimeTicks is > 0
                    ? TimeSpan.FromTicks(song.RunTimeTicks.Value)
                    : TimeSpan.Zero;

                _songRows.Add(new ArtistSongRow
                {
                    Id = song.Id!,
                    Number = (i + 1).ToString(),
                    Title = song.Name ?? "Unknown track",
                    Album = song.Album ?? string.Empty,
                    Artist = song.Artists?.FirstOrDefault() ?? song.AlbumArtist ?? ArtistTitle.Text,
                    Duration = duration > TimeSpan.Zero
                        ? duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss")
                        : string.Empty
                });
            }

            SongsList.ItemsSource = _songRows;
            SongCountText.Text = $"{_songs.Count} song{(_songs.Count == 1 ? "" : "s")}";
            ArtistMeta.Text = $"{albumCards.Count} album{(albumCards.Count == 1 ? "" : "s")} • {_songs.Count} song{(_songs.Count == 1 ? "" : "s")}";
            StatusText.Text = albumCards.Count == 0 && _songs.Count == 0
                ? "No music was returned for this artist."
                : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load artist: {ex.Message}";
        }
    }

    private void Album_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaCardViewModel album && !string.IsNullOrWhiteSpace(album.Id))
            Frame.Navigate(typeof(MusicAlbumPage), album.Id);
    }

    private async void Song_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ArtistSongRow row)
            return;

        var index = _songs.FindIndex(x => string.Equals(x.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        await App.MusicPlayback.PlayQueueAsync(_songs, index, shuffle: false);
    }

    private async void PlayArtist_Click(object sender, RoutedEventArgs e)
    {
        if (_songs.Count == 0) return;
        await App.MusicPlayback.PlayQueueAsync(_songs, 0, shuffle: false);
    }

    private async void ShuffleArtist_Click(object sender, RoutedEventArgs e)
    {
        if (_songs.Count == 0) return;
        await App.MusicPlayback.PlayQueueAsync(_songs, 0, shuffle: true);
    }

    private void MusicBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
