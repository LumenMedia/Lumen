using Microsoft.UI.Xaml.Controls;
using Lumen.Models;
using Lumen.ViewModels;
using Lumen.Views;

namespace Lumen.Services;

public static class QuickPlayService
{
    public static async Task PlayAsync(MediaCardViewModel item, Frame? frame)
    {
        if (frame is null || string.IsNullOrWhiteSpace(item.Id))
            return;

        try
        {
            if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
            {
                frame.Navigate(typeof(MusicPlayerPage), item.Id);
                return;
            }
            if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
            {
                var tracks = await App.Jellyfin.GetAlbumTracksAsync(item.Id);
                var playableTracks = tracks.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
                if (playableTracks.Count > 0)
                {
                    await App.MusicPlayback.PlayQueueAsync(playableTracks, 0, shuffle: false);
                    if (App.MusicPlayback.CurrentItem?.Id is string id)
                        frame.Navigate(typeof(MusicPlayerPage), id);
                }
                return;
            }
            if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
            {
                var songs = await App.Jellyfin.GetArtistSongsAsync(item.Id);
                var playableSongs = songs.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
                if (playableSongs.Count > 0)
                {
                    await App.MusicPlayback.PlayQueueAsync(playableSongs, 0, shuffle: false);
                    if (App.MusicPlayback.CurrentItem?.Id is string id)
                        frame.Navigate(typeof(MusicPlayerPage), id);
                }
                return;
            }
            BaseItemDto? playable = null;

            if (string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase))
            {
                // Reuse ItemPage playback selection so defaults stay consistent.
                var itemPage = new ItemPageViewModel();
                await itemPage.LoadAsync(item.Id);

                if (!itemPage.IsPlayable || string.IsNullOrWhiteSpace(itemPage.Id))
                    return;

                var selection = itemPage.BuildPlaybackSelection(startFromBeginning: false);

                // Keep the Home resume position as a fallback.
                if (selection.ResumePositionTicks <= 0 &&
                    !item.Played &&
                    item.ResumePositionTicks > 0)
                {
                    selection.ResumePositionTicks = item.ResumePositionTicks;
                }

                frame.Navigate(typeof(PlayerPage), selection);
                return;
            }
            else if (string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer Jellyfin Next Up for series quick play.
                var nextUp = await App.Jellyfin.GetUpNextAsync(100);
                playable = nextUp.Items.FirstOrDefault(x =>
                    string.Equals(x.SeriesId, item.Id, StringComparison.OrdinalIgnoreCase));

                // Fall back to the first episode for brand-new series.
                if (playable is null)
                {
                    var seasons = await App.Jellyfin.GetSeasonsAsync(item.Id);
                    var firstSeason = seasons.Items
                        .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                        .OrderBy(x => x.IndexNumber ?? int.MaxValue)
                        .FirstOrDefault();

                    if (firstSeason?.Id is not null)
                    {
                        var episodes = await App.Jellyfin.GetEpisodesAsync(firstSeason.Id);
                        playable = episodes.Items
                            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                            .OrderBy(x => x.IndexNumber ?? int.MaxValue)
                            .FirstOrDefault(x => x.UserData?.Played != true)
                            ?? episodes.Items.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Id));
                    }
                }
            }

            if (playable?.Id is null)
                return;

            var resumeTicks = playable.UserData?.Played == true
                ? 0
                : Math.Max(0, playable.UserData?.PlaybackPositionTicks ?? 0);

            var seriesSelection = new PlaybackSelection
            {
                ItemId = playable.Id,
                ResumePositionTicks = resumeTicks,
                StartFromBeginning = false
            };

            if (App.Settings.PreferDownloadedPlayback)
            {
                var local = App.Downloads.GetCompletedDownload(playable.Id);
                if (local is not null)
                {
                    seriesSelection.LocalFilePath = local.LocalPath;
                    seriesSelection.IsOffline = true;
                }
            }

            frame.Navigate(typeof(PlayerPage), seriesSelection);
        }
        catch
        {
        }
    }
}
