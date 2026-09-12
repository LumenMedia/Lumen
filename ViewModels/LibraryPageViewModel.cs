using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Services;

namespace Lumen.ViewModels;

public partial class LibraryPageViewModel : ObservableObject
{
    private LibraryViewModel? _library;
    private CancellationTokenSource? _loadCts;
    private readonly List<MediaCardViewModel> _allItems = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial int TotalCount { get; set; }
    [ObservableProperty]
    public partial string SelectedSort { get; set; } = "Name";
    [ObservableProperty]
    public partial bool Ascending { get; set; } = true;
    [ObservableProperty]
    public partial string SelectedGenre { get; set; } = "All genres";
    [ObservableProperty]
    public partial string SelectedYear { get; set; } = "All years";
    [ObservableProperty]
    public partial string SelectedFilter { get; set; } = "Everything";
    [ObservableProperty]
    public partial bool IsMusicLibrary { get; set; }
    [ObservableProperty]
    public partial string SelectedMusicView { get; set; } = "Albums";
    [ObservableProperty]
    public partial bool ShowStandardGrid { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowMusicGrid { get; set; }
    [ObservableProperty]
    public partial bool ShowMusicSongs { get; set; }

    public ObservableCollection<MediaCardViewModel> Items { get; } = [];
    public ObservableCollection<string> Genres { get; } = ["All genres"];
    public ObservableCollection<string> Years { get; } = ["All years"];
    public ObservableCollection<string> Filters { get; } = [];
    public ObservableCollection<string> MusicViews { get; } = ["Albums", "Artists", "Songs"];
    public ObservableCollection<string> SortOptions { get; } = [];

    public void SetLibrary(LibraryViewModel library)
    {
        _library = library;
        _allItems.Clear();
        Items.Clear();
        TotalCount = 0;
        StatusMessage = string.Empty;
        SelectedGenre = "All genres";
        SelectedYear = "All years";
        SelectedFilter = "Everything";
        SelectedSort = "Name";
        Ascending = true;
        IsMusicLibrary = string.Equals(library.Type, "music", StringComparison.OrdinalIgnoreCase);
        SelectedMusicView = "Albums";

        Filters.Clear();
        SortOptions.Clear();
        if (IsMusicLibrary)
        {
            foreach (var value in new[] { "Everything", "Favourites" }) Filters.Add(value);
            foreach (var value in new[] { "Name", "Release date", "Recently added", "Runtime" }) SortOptions.Add(value);
        }
        else
        {
            foreach (var value in new[] { "Everything", "Unplayed", "Played", "Favourites", "4K", "1080p", "720p" }) Filters.Add(value);
            foreach (var value in new[] { "Name", "Release date", "Recently added", "Rating", "Runtime" }) SortOptions.Add(value);
        }

        UpdateMusicViewState();
    }

    private void UpdateMusicViewState()
    {
        ShowStandardGrid = !IsMusicLibrary;
        ShowMusicSongs = IsMusicLibrary &&
            string.Equals(SelectedMusicView, "Songs", StringComparison.OrdinalIgnoreCase);
        ShowMusicGrid = IsMusicLibrary && !ShowMusicSongs;
    }

    public void SetMusicView(string view)
    {
        SelectedMusicView = view;
        UpdateMusicViewState();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_library is null) return;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _loadCts.Token;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            UpdateMusicViewState();
            var result = IsMusicLibrary
                ? await App.Jellyfin.GetMusicItemsAsync(_library.Id, SelectedMusicView, 500, token)
                : await App.Jellyfin.GetLibraryItemsAsync(_library.Id, 400, "SortName", "Ascending", token);

            _allItems.Clear();
            Genres.Clear(); Genres.Add("All genres");
            Years.Clear(); Years.Add("All years");

            var genreSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var yearSet = new SortedSet<int>(Comparer<int>.Create((a,b) => b.CompareTo(a)));

            foreach (var item in result.Items.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name)))
            {
                var card = MediaCardViewModel.FromDto(item);
                card.ImageUrl = App.Jellyfin.GetImageUrl(item.Id!, "Primary",
                    item.ImageTags?.GetValueOrDefault("Primary") ?? item.PrimaryImageTag, 420, 88);
                _allItems.Add(card);
                foreach (var genre in (item.Genres ?? []).Where(g => !string.IsNullOrWhiteSpace(g)))
                    genreSet.Add(genre);
                if (card.Year is > 0) yearSet.Add(card.Year.Value);
            }

            foreach (var genre in genreSet) Genres.Add(genre);
            foreach (var year in yearSet) Years.Add(year.ToString());

            // Reassert the actual collection items after WinUI has seen the rebuilt
            // ItemsSource. This avoids the ComboBox placeholder being pushed back
            // into the bound selection during initial population.
            SelectedGenre = "All genres";
            SelectedYear = "All years";
            SelectedFilter = "Everything";
            if (!SortOptions.Contains(SelectedSort))
                SelectedSort = "Name";

            ApplyFilters();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { StatusMessage = ConnectionErrorMapper.ToFriendlyMessage(ex, App.MainViewModel.ServerUrl); }
        finally { IsBusy = false; }
    }

    public void ApplyFilters()
    {
        IEnumerable<MediaCardViewModel> query = _allItems;

        var genre = SelectedGenre?.Trim();
        if (!string.IsNullOrWhiteSpace(genre) &&
            !string.Equals(genre, "All genres", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(genre, "Genre", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.Genres.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Any(g => string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)));
        }

        if (SelectedYear != "All years" && int.TryParse(SelectedYear, out var year))
            query = query.Where(x => x.Year == year);

        query = SelectedFilter switch
        {
            "Unplayed" => query.Where(x => !x.Played),
            "Played" => query.Where(x => x.Played),
            "Favourites" => query.Where(x => x.IsFavorite),
            "4K" => query.Where(x => x.ResolutionHeight >= 2160),
            "1080p" => query.Where(x => x.ResolutionHeight >= 1080 && x.ResolutionHeight < 2160),
            "720p" => query.Where(x => x.ResolutionHeight >= 720 && x.ResolutionHeight < 1080),
            _ => query
        };

        query = SelectedSort switch
        {
            "Release date" => Ascending ? query.OrderBy(x => x.Year ?? 0) : query.OrderByDescending(x => x.Year ?? 0),
            "Recently added" => Ascending ? query.OrderBy(x => x.DateCreated) : query.OrderByDescending(x => x.DateCreated),
            "Rating" => Ascending ? query.OrderBy(x => x.Rating ?? 0) : query.OrderByDescending(x => x.Rating ?? 0),
            "Runtime" => Ascending ? query.OrderBy(x => x.RuntimeTicks) : query.OrderByDescending(x => x.RuntimeTicks),
            _ => Ascending ? query.OrderBy(x => x.Name) : query.OrderByDescending(x => x.Name)
        };

        Items.Clear();
        foreach (var item in query) Items.Add(item);
        TotalCount = Items.Count;
        StatusMessage = Items.Count == 0 ? (IsMusicLibrary ? "No music matches these filters." : "No titles match these filters.") : string.Empty;
    }

    public void ToggleSortDirection()
    {
        Ascending = !Ascending;
        ApplyFilters();
    }
}
