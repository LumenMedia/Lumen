using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Lumen.Models;
using Lumen.ViewModels;

namespace Lumen.Views;

public sealed class MusicLibraryCard
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
}

public sealed class MusicSongRow
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string Year { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
}

public sealed class MusicGenreRow
{
    public string Name { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
}

public sealed record MusicLibraryNavigationRequest(LibraryViewModel Library, string View);

public sealed partial class MusicLibraryPage : Page
{
    private LibraryViewModel? _library;
    private readonly List<BaseItemDto> _albums = [];
    private readonly List<BaseItemDto> _artists = [];
    private readonly List<BaseItemDto> _songs = [];
    private readonly List<BaseItemDto> _playlists = [];
    private readonly List<BaseItemDto> _recentlyAdded = [];
    private readonly List<BaseItemDto> _recentlyPlayed = [];
    private readonly List<BaseItemDto> _favouriteAlbums = [];
    private readonly List<BaseItemDto> _favouriteSongs = [];
    private readonly List<BaseItemDto> _favouriteArtists = [];
    private readonly List<BaseItemDto> _searchSongs = [];
    private bool _loaded;

    public MusicLibraryPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var requestedView = "Home";
        LibraryViewModel? library = null;
        if (e.Parameter is MusicLibraryNavigationRequest request)
        {
            library = request.Library;
            requestedView = string.IsNullOrWhiteSpace(request.View) ? "Home" : request.View;
        }
        else if (e.Parameter is LibraryViewModel directLibrary)
        {
            library = directLibrary;
        }

        if (library is null) return;

        _library = library;
        MusicLibraryName.Text = library.Name;
        if (!_loaded)
        {
            await LoadMusicAsync();
            if (!string.Equals(requestedView, "Home", StringComparison.OrdinalIgnoreCase))
                ShowView(requestedView);
        }
        else
        {
            ShowView(requestedView);
        }
    }

    public void ShowViewFromShell(string view) => ShowView(view);

    private async Task LoadMusicAsync()
    {
        if (_library is null) return;

        SetBusy(true);
        StatusText.Text = string.Empty;
        try
        {
            var albumsTask = SafeQueryAsync(() => App.Jellyfin.GetMusicItemsAsync(_library.Id, "Albums", 1200));
            var artistsTask = SafeQueryAsync(() => App.Jellyfin.GetMusicItemsAsync(_library.Id, "Artists", 1200));
            var songsTask = SafeQueryAsync(() => App.Jellyfin.GetMusicItemsAsync(_library.Id, "Songs", 2500));
            var playlistsTask = SafeQueryAsync(() => App.Jellyfin.GetMusicPlaylistsAsync(400));
            var recentTask = SafeQueryAsync(() => App.Jellyfin.GetMusicRecentlyAddedAlbumsAsync(_library.Id, 30));
            var playedTask = SafeQueryAsync(() => App.Jellyfin.GetMusicRecentlyPlayedAsync(_library.Id, 30));
            var favouriteAlbumsTask = SafeQueryAsync(() => App.Jellyfin.GetFavoriteMusicAlbumsAsync(_library.Id, 40));
            var favouriteSongsTask = SafeQueryAsync(() => App.Jellyfin.GetFavoriteMusicSongsAsync(_library.Id, 250));

            await Task.WhenAll(albumsTask, artistsTask, songsTask, playlistsTask,
                recentTask, playedTask, favouriteAlbumsTask, favouriteSongsTask);

            Replace(_albums, albumsTask.Result.Items);
            Replace(_artists, artistsTask.Result.Items);
            Replace(_songs, songsTask.Result.Items);
            Replace(_playlists, playlistsTask.Result.Items);
            Replace(_recentlyAdded, recentTask.Result.Items);
            Replace(_recentlyPlayed, playedTask.Result.Items.Where(x => (x.UserData?.PlayCount ?? 0) > 0));
            Replace(_favouriteAlbums, favouriteAlbumsTask.Result.Items);
            Replace(_favouriteSongs, favouriteSongsTask.Result.Items);
            Replace(_favouriteArtists, _artists.Where(x => x.UserData?.IsFavorite == true));

            HomeRecentlyPlayedList.ItemsSource = _recentlyPlayed.Take(16).Select(ToTrackCard).ToList();
            HomeRecentlyAddedList.ItemsSource = _recentlyAdded.Take(18).Select(ToAlbumCard).ToList();
            HomeFavouriteAlbumsList.ItemsSource = _favouriteAlbums.Take(18).Select(ToAlbumCard).ToList();
            HomeFavouriteArtistsList.ItemsSource = _favouriteArtists.Take(18).Select(ToArtistCard).ToList();
            HomePlaylistsList.ItemsSource = _playlists.Take(18).Select(ToPlaylistCard).ToList();

            HomeFavouriteSection.Visibility = _favouriteAlbums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            HomeFavouriteArtistsSection.Visibility = _favouriteArtists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            HomePlaylistSection.Visibility = _playlists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentlyPlayedSection.Visibility = _recentlyPlayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            AlbumsGrid.ItemsSource = _albums.Select(ToAlbumCard).ToList();
            ArtistsGrid.ItemsSource = _artists.Select(ToArtistCard).ToList();
            SongsList.ItemsSource = ToSongRows(_songs);
            PlaylistsGrid.ItemsSource = _playlists.Select(ToPlaylistCard).ToList();
            GenresGrid.ItemsSource = BuildGenreRows();
            FavouriteAlbumsGrid.ItemsSource = _favouriteAlbums.Select(ToAlbumCard).ToList();
            FavouriteArtistsGrid.ItemsSource = _favouriteArtists.Select(ToArtistCard).ToList();
            FavouriteSongsList.ItemsSource = ToSongRows(_favouriteSongs);

            FavouriteAlbumsSection.Visibility = _favouriteAlbums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FavouriteArtistsSection.Visibility = _favouriteArtists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FavouriteSongsSection.Visibility = _favouriteSongs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _loaded = true;
            ShowView("Home");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load the music library: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static async Task<QueryResult<BaseItemDto>> SafeQueryAsync(Func<Task<QueryResult<BaseItemDto>>> action)
    {
        try { return await action(); }
        catch { return new QueryResult<BaseItemDto>(); }
    }

    private static void Replace(List<BaseItemDto> target, IEnumerable<BaseItemDto> source)
    {
        target.Clear();
        target.AddRange(source.Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name)));
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ContentScroll.Opacity = busy ? .38 : 1;
        ContentScroll.IsHitTestVisible = !busy;
    }

    private void MusicNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string view })
            ShowView(view);
    }

    private void ShowView(string view)
    {
        HideAllPanels();
        StatusText.Text = string.Empty;
        UpdateNavigationSelection(view);

        switch (view)
        {
            case "Recently Added":
                SectionTitle.Text = "Recently Added";
                SectionSubtitle.Text = $"Newest albums in {MusicLibraryName.Text}";
                AlbumsGrid.ItemsSource = _recentlyAdded.Select(ToAlbumCard).ToList();
                AlbumsPanel.Visibility = Visibility.Visible;
                break;
            case "Artists":
                SectionTitle.Text = "Artists";
                SectionSubtitle.Text = $"{_artists.Count:N0} artists";
                ArtistsGrid.ItemsSource = _artists.Select(ToArtistCard).ToList();
                ArtistsPanel.Visibility = Visibility.Visible;
                break;
            case "Albums":
                SectionTitle.Text = "Albums";
                SectionSubtitle.Text = $"{_albums.Count:N0} albums";
                AlbumsGrid.ItemsSource = _albums.Select(ToAlbumCard).ToList();
                AlbumsPanel.Visibility = Visibility.Visible;
                break;
            case "Songs":
                SectionTitle.Text = "Songs";
                SectionSubtitle.Text = $"{_songs.Count:N0} songs • click a row to play without leaving your library";
                SongsList.ItemsSource = ToSongRows(_songs);
                SongsPanel.Visibility = Visibility.Visible;
                break;
            case "Playlists":
                SectionTitle.Text = "Playlists";
                SectionSubtitle.Text = $"{_playlists.Count:N0} playlists";
                PlaylistsPanel.Visibility = Visibility.Visible;
                break;
            case "Genres":
                SectionTitle.Text = "Genres";
                SectionSubtitle.Text = "Browse your collection by genre";
                GenresPanel.Visibility = Visibility.Visible;
                break;
            case "Favourites":
                SectionTitle.Text = "Favourites";
                SectionSubtitle.Text = "Albums, artists and songs you have favourited in Jellyfin";
                FavouritesPanel.Visibility = Visibility.Visible;
                if (_favouriteAlbums.Count == 0 && _favouriteArtists.Count == 0 && _favouriteSongs.Count == 0)
                    StatusText.Text = "You don't have any favourite music yet.";
                break;
            default:
                SectionTitle.Text = "Home";
                SectionSubtitle.Text = "Recently played, new additions, favourites and playlists";
                HomePanel.Visibility = Visibility.Visible;
                break;
        }

        ContentScroll.ChangeView(null, 0, null, true);
    }

    private void HideAllPanels()
    {
        HomePanel.Visibility = Visibility.Collapsed;
        AlbumsPanel.Visibility = Visibility.Collapsed;
        ArtistsPanel.Visibility = Visibility.Collapsed;
        SongsPanel.Visibility = Visibility.Collapsed;
        PlaylistsPanel.Visibility = Visibility.Collapsed;
        GenresPanel.Visibility = Visibility.Collapsed;
        FavouritesPanel.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateNavigationSelection(string activeView)
    {
        if (App.MainWindow is MainWindow window)
            window.SetMusicNavigationSelection(activeView);
    }

    private async void MusicSearch_Click(object sender, RoutedEventArgs e) => await SearchMusicAsync();

    private async void MusicSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        await SearchMusicAsync();
        e.Handled = true;
    }

    private Task SearchMusicAsync()
    {
        var query = MusicSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowView("Home");
            return Task.CompletedTask;
        }

        HideAllPanels();
        UpdateNavigationSelection(string.Empty);
        SectionTitle.Text = $"Search: {query}";
        SectionSubtitle.Text = $"Results from {MusicLibraryName.Text}";

        static bool Has(string? value, string q) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(q, StringComparison.OrdinalIgnoreCase);

        var artists = _artists.Where(x => Has(x.Name, query)).ToList();
        var albums = _albums.Where(x => Has(x.Name, query) || Has(x.AlbumArtist, query) || (x.Artists ?? []).Any(a => Has(a, query))).ToList();
        _searchSongs.Clear();
        _searchSongs.AddRange(_songs.Where(x => Has(x.Name, query) || Has(x.Album, query) || Has(x.AlbumArtist, query) || (x.Artists ?? []).Any(a => Has(a, query))));

        SearchArtistsGrid.ItemsSource = artists.Select(ToArtistCard).ToList();
        SearchAlbumsGrid.ItemsSource = albums.Select(ToAlbumCard).ToList();
        SearchSongsList.ItemsSource = ToSongRows(_searchSongs);
        SearchArtistsSection.Visibility = artists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchAlbumsSection.Visibility = albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchSongsSection.Visibility = _searchSongs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Visible;
        StatusText.Text = artists.Count == 0 && albums.Count == 0 && _searchSongs.Count == 0
            ? "No artists, albums or songs matched your search."
            : string.Empty;
        ContentScroll.ChangeView(null, 0, null, true);
        return Task.CompletedTask;
    }

    private void Album_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicLibraryCard card && !string.IsNullOrWhiteSpace(card.Id))
            Frame.Navigate(typeof(MusicAlbumPage), card.Id);
    }

    private void Artist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicLibraryCard card && !string.IsNullOrWhiteSpace(card.Id))
            Frame.Navigate(typeof(ArtistPage), card.Id);
    }

    private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicLibraryCard card && !string.IsNullOrWhiteSpace(card.Id))
            Frame.Navigate(typeof(MusicPlaylistPage), card.Id);
    }

    private async void TrackCard_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicLibraryCard card)
            await PlayTrackAsync(card.Id, _recentlyPlayed);
    }

    private async void Song_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicSongRow row)
            await PlayTrackAsync(row.Id, _songs);
    }

    private async void FavouriteSong_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicSongRow row)
            await PlayTrackAsync(row.Id, _favouriteSongs);
    }

    private async void SearchSong_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MusicSongRow row)
            await PlayTrackAsync(row.Id, _searchSongs);
    }

    private async Task PlayTrackAsync(string id, IReadOnlyList<BaseItemDto> source)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var queue = source.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
        var index = queue.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            await App.MusicPlayback.PlayQueueAsync(queue, index, shuffle: false);
        else
            await App.MusicPlayback.PlayItemAsync(id);
    }

    private async void ShuffleLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_songs.Count == 0) return;
        await App.MusicPlayback.PlayQueueAsync(_songs, 0, shuffle: true);
    }

    private void Genre_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MusicGenreRow genre) return;
        var matches = _albums.Where(x => (x.Genres ?? []).Any(g =>
            string.Equals(g, genre.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        HideAllPanels();
        UpdateNavigationSelection("Genres");
        SectionTitle.Text = genre.Name;
        SectionSubtitle.Text = $"{matches.Count:N0} album{(matches.Count == 1 ? "" : "s")}";
        AlbumsGrid.ItemsSource = matches.Select(ToAlbumCard).ToList();
        AlbumsPanel.Visibility = Visibility.Visible;
        StatusText.Text = matches.Count == 0 ? "No albums were returned for this genre." : string.Empty;
        ContentScroll.ChangeView(null, 0, null, true);
    }

    private List<MusicGenreRow> BuildGenreRows()
    {
        var counts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _albums.Concat(_songs))
        {
            foreach (var genre in item.Genres ?? [])
            {
                if (string.IsNullOrWhiteSpace(genre) || string.IsNullOrWhiteSpace(item.Id)) continue;
                if (!counts.TryGetValue(genre.Trim(), out var ids))
                    counts[genre.Trim()] = ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ids.Add(item.Id!);
            }
        }

        return counts.OrderBy(x => x.Key)
            .Select(x => new MusicGenreRow
            {
                Name = x.Key,
                CountText = $"{x.Value.Count:N0} item{(x.Value.Count == 1 ? "" : "s")}" 
            }).ToList();
    }

    private static List<MusicSongRow> ToSongRows(IReadOnlyList<BaseItemDto> source)
    {
        var rows = new List<MusicSongRow>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var runtime = item.RunTimeTicks is > 0 ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : TimeSpan.Zero;
            rows.Add(new MusicSongRow
            {
                Id = item.Id ?? string.Empty,
                Number = (i + 1).ToString(),
                Title = item.Name ?? "Unknown track",
                Artist = item.Artists?.FirstOrDefault() ?? item.AlbumArtist ?? "Unknown artist",
                Album = item.Album ?? string.Empty,
                Year = item.ProductionYear?.ToString() ?? string.Empty,
                Duration = runtime > TimeSpan.Zero
                    ? runtime.ToString(runtime.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss")
                    : string.Empty
            });
        }
        return rows;
    }

    private static MusicLibraryCard ToAlbumCard(BaseItemDto item)
    {
        var artist = item.AlbumArtist ?? item.Artists?.FirstOrDefault() ?? string.Empty;
        var subtitle = item.ProductionYear is > 0 && !string.IsNullOrWhiteSpace(artist)
            ? $"{artist} • {item.ProductionYear}"
            : artist;
        return new MusicLibraryCard
        {
            Id = item.Id ?? string.Empty,
            Type = "MusicAlbum",
            Name = item.Name ?? "Album",
            Subtitle = subtitle,
            ImageUrl = ImageUrl(item, item.Id)
        };
    }

    private static MusicLibraryCard ToArtistCard(BaseItemDto item) => new()
    {
        Id = item.Id ?? string.Empty,
        Type = "MusicArtist",
        Name = item.Name ?? "Artist",
        Subtitle = "Artist",
        ImageUrl = ImageUrl(item, item.Id)
    };

    private static MusicLibraryCard ToPlaylistCard(BaseItemDto item) => new()
    {
        Id = item.Id ?? string.Empty,
        Type = "Playlist",
        Name = item.Name ?? "Playlist",
        Subtitle = "Playlist",
        ImageUrl = ImageUrl(item, item.Id)
    };

    private static MusicLibraryCard ToTrackCard(BaseItemDto item) => new()
    {
        Id = item.Id ?? string.Empty,
        Type = "Audio",
        Name = item.Name ?? "Unknown track",
        Subtitle = item.Artists?.FirstOrDefault() ?? item.AlbumArtist ?? item.Album ?? string.Empty,
        ImageUrl = ImageUrl(item, item.AlbumId ?? item.Id)
    };

    private static string ImageUrl(BaseItemDto item, string? imageItemId)
    {
        if (string.IsNullOrWhiteSpace(imageItemId)) return string.Empty;
        var tag = item.ImageTags?.GetValueOrDefault("Primary") ?? item.PrimaryImageTag;
        return App.Jellyfin.GetImageUrl(imageItemId, "Primary", tag, 480, 90);
    }
}
