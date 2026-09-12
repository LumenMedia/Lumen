using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;
using Lumen.Services;

namespace Lumen.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private static DateTime _lastSuccessfulLoadUtc;
    public bool HasFreshCache => FeaturedItems.Count > 0 &&
        DateTime.UtcNow - _lastSuccessfulLoadUtc < TimeSpan.FromSeconds(90);

    public static void InvalidateCache()
        => _lastSuccessfulLoadUtc = DateTime.MinValue;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasContinueWatching { get; set; }
    [ObservableProperty]
    public partial bool HasUpNext { get; set; }
    [ObservableProperty]
    public partial bool HasFavorites { get; set; }
    [ObservableProperty]
    public partial bool HasBothPersonalizedRows { get; set; }
    [ObservableProperty]
    public partial bool HasRecentlyReleasedMovies { get; set; }
    [ObservableProperty]
    public partial bool HasRecentlyReleasedShows { get; set; }
    [ObservableProperty]
    public partial bool HasRecentlyAddedMovies { get; set; }
    [ObservableProperty]
    public partial bool HasRecentlyAddedShows { get; set; }
    [ObservableProperty]
    public partial bool HasError { get; set; }
    [ObservableProperty]
    public partial MediaCardViewModel? FeaturedItem { get; set; }
    [ObservableProperty]
    public partial bool HasFeatured { get; set; }
    [ObservableProperty]
    public partial int FeaturedIndex { get; set; }
    public string FeaturedPositionText => FeaturedItems.Count == 0 ? string.Empty : $"{FeaturedIndex + 1} / {FeaturedItems.Count}";

    public ObservableCollection<MediaCardViewModel> FeaturedItems { get; } = [];
    public ObservableCollection<MediaCardViewModel> ContinueWatching { get; } = [];
    public ObservableCollection<MediaCardViewModel> UpNext { get; } = [];
    public ObservableCollection<MediaCardViewModel> Favorites { get; } = [];
    public ObservableCollection<MediaCardViewModel> RecentlyReleasedMovies { get; } = [];
    public ObservableCollection<MediaCardViewModel> RecentlyReleasedShows { get; } = [];
    public ObservableCollection<MediaCardViewModel> RecentlyAddedMovies { get; } = [];
    public ObservableCollection<MediaCardViewModel> RecentlyAddedShows { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!App.Jellyfin.IsAuthenticated) return;

        IsBusy = true;
        StatusMessage = string.Empty;
        HasError = false;
        try
        {
            FeaturedItems.Clear();
            ContinueWatching.Clear();
            UpNext.Clear();
        Favorites.Clear();
            RecentlyReleasedMovies.Clear();
            RecentlyReleasedShows.Clear();
            RecentlyAddedMovies.Clear();
            RecentlyAddedShows.Clear();

            // Keep Home sections independent so one failed response cannot blank the page.
            var continueResultTask = App.Settings.ShowContinueWatching
                ? SafeQueryAsync(() => App.Jellyfin.GetContinueWatchingAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var nextUpResultTask = App.Settings.ShowUpNext
                ? SafeQueryAsync(() => App.Jellyfin.GetUpNextAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var favoritesResultTask = App.Settings.ShowFavorites
                ? SafeQueryAsync(() => App.Jellyfin.GetFavoriteItemsAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var releasedMoviesResultTask = App.Settings.ShowRecentlyReleased
                ? SafeQueryAsync(() => App.Jellyfin.GetRecentlyReleasedMoviesAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var releasedShowsResultTask = App.Settings.ShowRecentlyReleased
                ? SafeQueryAsync(() => App.Jellyfin.GetRecentlyReleasedSeriesAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var addedMoviesResultTask = App.Settings.ShowRecentlyAdded
                ? SafeQueryAsync(() => App.Jellyfin.GetLatestMoviesAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());
            var addedShowsResultTask = App.Settings.ShowRecentlyAdded
                ? SafeQueryAsync(() => App.Jellyfin.GetLatestSeriesAsync(20, ct))
                : Task.FromResult(new QueryResult<BaseItemDto>());

            await Task.WhenAll(
                continueResultTask,
                nextUpResultTask,
                favoritesResultTask,
                releasedMoviesResultTask,
                releasedShowsResultTask,
                addedMoviesResultTask,
                addedShowsResultTask);

            AddContinueWatching(continueResultTask.Result.Items);
            AddUpNext(nextUpResultTask.Result.Items);
            AddCards(Favorites, favoritesResultTask.Result.Items);
            AddCards(RecentlyReleasedMovies, releasedMoviesResultTask.Result.Items);
            AddCards(RecentlyReleasedShows, releasedShowsResultTask.Result.Items);
            AddCards(RecentlyAddedMovies, addedMoviesResultTask.Result.Items);
            AddCards(RecentlyAddedShows, addedShowsResultTask.Result.Items);

            var featuredCandidates = RecentlyReleasedMovies
                .Concat(RecentlyReleasedShows)
                .Concat(RecentlyAddedMovies)
                .Concat(RecentlyAddedShows)
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(8)
                .ToList();

            // Keep a hero pool even when release/addition rows are hidden.
            if (featuredCandidates.Count == 0)
            {
                var fallbackMoviesTask = SafeQueryAsync(() => App.Jellyfin.GetLatestMoviesAsync(6, ct));
                var fallbackShowsTask = SafeQueryAsync(() => App.Jellyfin.GetLatestSeriesAsync(6, ct));
                await Task.WhenAll(fallbackMoviesTask, fallbackShowsTask);
                featuredCandidates = fallbackMoviesTask.Result.Items
                    .Concat(fallbackShowsTask.Result.Items)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .Select(MediaCardViewModel.FromDto)
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Take(8)
                    .ToList();
            }

            // Hydrate hero candidates concurrently while preserving their order.
            var hydratedCards = await Task.WhenAll(featuredCandidates.Select(async card =>
            {
                try
                {
                    var hydrated = await App.Jellyfin.GetItemAsync(card.Id, ct);
                    if (hydrated is null) return card;

                    var hydratedCard = MediaCardViewModel.FromDto(hydrated);
                    hydratedCard.ImageUrl = App.Jellyfin.GetImageUrl(hydrated.Id!, "Primary", hydrated.PrimaryImageTag, 520, 90);
                    hydratedCard.BackdropUrl = GetBackdropUrl(hydrated);
                    return hydratedCard;
                }
                catch
                {
                    return card;
                }
            }));

            foreach (var card in hydratedCards)
                FeaturedItems.Add(card);

            FeaturedIndex = 0;
            FeaturedItem = FeaturedItems.FirstOrDefault();
            OnPropertyChanged(nameof(FeaturedPositionText));
            HasFeatured = FeaturedItems.Count > 0;

            HasContinueWatching = ContinueWatching.Count > 0;
            HasUpNext = UpNext.Count > 0;
            HasFavorites = Favorites.Count > 0;
            HasBothPersonalizedRows = HasContinueWatching && HasUpNext;
            HasRecentlyReleasedMovies = RecentlyReleasedMovies.Count > 0;
            HasRecentlyReleasedShows = RecentlyReleasedShows.Count > 0;
            HasRecentlyAddedMovies = RecentlyAddedMovies.Count > 0;
            HasRecentlyAddedShows = RecentlyAddedShows.Count > 0;

            if (!HasFeatured && !HasContinueWatching && !HasUpNext && !HasFavorites &&
                !HasRecentlyReleasedMovies && !HasRecentlyReleasedShows &&
                !HasRecentlyAddedMovies && !HasRecentlyAddedShows)
            {
                StatusMessage = "No Home content is available for this user.";
            }
            _lastSuccessfulLoadUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = ConnectionErrorMapper.ToFriendlyMessage(ex, App.MainViewModel.ServerUrl);
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<QueryResult<BaseItemDto>> SafeQueryAsync(Func<Task<QueryResult<BaseItemDto>>> query)
    {
        try
        {
            return await query();
        }
        catch
        {
            return new QueryResult<BaseItemDto>();
        }
    }

    public void UpdateContinueWatchingProgress(string itemId, long positionTicks, long runtimeTicks)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return;

        var card = ContinueWatching.FirstOrDefault(x =>
            string.Equals(x.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (card is null) return;

        var safePosition = Math.Max(0, positionTicks);
        var safeRuntime = Math.Max(0, runtimeTicks);

        card.ResumePositionTicks = safePosition;
        if (safeRuntime > 0)
            card.RuntimeTicks = safeRuntime;

        if (card.RuntimeTicks > 0)
        {
            card.Progress = Math.Clamp(
                safePosition / (double)card.RuntimeTicks * 100d,
                0d,
                100d);

            var remaining = TimeSpan.FromTicks(Math.Max(0, card.RuntimeTicks - safePosition));
            card.RemainingText = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m left"
                : $"{Math.Max(1, remaining.Minutes)} min left";
        }
    }

    private void AddContinueWatching(IEnumerable<BaseItemDto> items)
    {
        foreach (var item in items.Where(i =>
                     !string.IsNullOrWhiteSpace(i.Id) &&
                     !string.IsNullOrWhiteSpace(i.Name)))
        {
            var userData = item.UserData;
            if (userData is not null && (userData.Played || userData.PlaybackPositionTicks <= 0))
                continue;

            var card = MediaCardViewModel.FromDto(item);
            var imageId = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesId)
                ? item.SeriesId!
                : item.Id!;
            var imageTag = imageId == item.Id ? item.PrimaryImageTag : null;
            card.ImageUrl = App.Jellyfin.GetImageUrl(imageId, "Primary", imageTag, 420, 90);
            card.BackdropUrl = GetBackdropUrl(item);
            card.Subtitle = item.Type == "Episode" ? BuildEpisodeSubtitle(item) : card.Subtitle;
            ContinueWatching.Add(card);
            if (ContinueWatching.Count >= 20) break;
        }
    }

    private void AddUpNext(IEnumerable<BaseItemDto> items)
    {
        foreach (var item in items.Where(i =>
                     !string.IsNullOrWhiteSpace(i.Id) &&
                     !string.IsNullOrWhiteSpace(i.Name)))
        {
            var card = MediaCardViewModel.FromDto(item);
            var imageId = item.Type == "Episode" && !string.IsNullOrWhiteSpace(item.SeriesId)
                ? item.SeriesId!
                : item.Id!;
            var imageTag = imageId == item.Id ? item.PrimaryImageTag : null;
            card.ImageUrl = App.Jellyfin.GetImageUrl(imageId, "Primary", imageTag, 420, 90);
            card.BackdropUrl = GetBackdropUrl(item);
            card.Subtitle = item.Type == "Episode" ? BuildEpisodeSubtitle(item) : card.Subtitle;
            UpNext.Add(card);
            if (UpNext.Count >= 20) break;
        }
    }

    private static void AddCards(ObservableCollection<MediaCardViewModel> target, IEnumerable<BaseItemDto> items)
    {
        foreach (var item in items.Where(i =>
                     !string.IsNullOrWhiteSpace(i.Id) &&
                     !string.IsNullOrWhiteSpace(i.Name)))
        {
            var card = MediaCardViewModel.FromDto(item);
            card.ImageUrl = App.Jellyfin.GetImageUrl(item.Id!, "Primary", item.PrimaryImageTag, 420, 90);
            card.BackdropUrl = GetBackdropUrl(item);
            target.Add(card);
        }
    }

    private static string GetBackdropUrl(BaseItemDto item)
    {
        var tag = item.BackdropImageTags?.FirstOrDefault();
        return App.Jellyfin.GetImageUrl(item.Id!, "Backdrop", tag, 1920, 90);
    }

    public void ShowNextFeatured()
    {
        if (FeaturedItems.Count == 0) return;
        FeaturedIndex = (FeaturedIndex + 1) % FeaturedItems.Count;
        FeaturedItem = FeaturedItems[FeaturedIndex];
        OnPropertyChanged(nameof(FeaturedPositionText));
    }

    public void ShowPreviousFeatured()
    {
        if (FeaturedItems.Count == 0) return;
        FeaturedIndex = (FeaturedIndex - 1 + FeaturedItems.Count) % FeaturedItems.Count;
        FeaturedItem = FeaturedItems[FeaturedIndex];
        OnPropertyChanged(nameof(FeaturedPositionText));
    }

    private static string BuildEpisodeSubtitle(BaseItemDto item)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.SeriesName)) bits.Add(item.SeriesName!);
        if (item.ParentIndexNumber is not null) bits.Add($"S{item.ParentIndexNumber.Value:00}");
        if (item.IndexNumber is not null) bits.Add($"E{item.IndexNumber.Value:00}");
        if (!string.IsNullOrWhiteSpace(item.Name)) bits.Add(item.Name!);
        return string.Join(" • ", bits);
    }
}
