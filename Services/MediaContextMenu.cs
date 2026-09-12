using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lumen.Models;
using Lumen.ViewModels;
using Lumen.Views;

namespace Lumen.Services;

public static class MediaContextMenu
{
    public static void Show(FrameworkElement target, MediaCardViewModel item, Frame? frame)
    {
        if (string.IsNullOrWhiteSpace(item.Id)) return;

        var flyout = new MenuFlyout();

        var details = new MenuFlyoutItem { Text = "View details" };
        details.Click += (_, _) =>
        {
            if (string.Equals(item.Type, "MusicArtist", StringComparison.OrdinalIgnoreCase))
                frame?.Navigate(typeof(ArtistPage), item.Id);
            else if (string.Equals(item.Type, "MusicAlbum", StringComparison.OrdinalIgnoreCase))
                frame?.Navigate(typeof(MusicAlbumPage), item.Id);
            else if (string.Equals(item.Type, "Audio", StringComparison.OrdinalIgnoreCase))
                frame?.Navigate(typeof(MusicPlayerPage), item.Id);
            else
                frame?.Navigate(typeof(ItemPage), item.Id);
        };
        flyout.Items.Add(details);

        var play = new MenuFlyoutItem { Text = item.HasProgress ? "Resume" : "Play" };
        play.Click += async (_, _) =>
        {
            try { await QuickPlayService.PlayAsync(item, frame); }
            catch { }
        };
        flyout.Items.Add(play);

        if (item.HasProgress &&
            (string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase)))
        {
            var beginning = new MenuFlyoutItem { Text = "Play from beginning" };
            beginning.Click += (_, _) =>
            {
                var selection = new PlaybackSelection
                {
                    ItemId = item.Id,
                    ResumePositionTicks = 0,
                    StartFromBeginning = true
                };
                if (App.Settings.PreferDownloadedPlayback)
                {
                    var local = App.Downloads.GetCompletedDownload(item.Id);
                    if (local is not null)
                    {
                        selection.LocalFilePath = local.LocalPath;
                        selection.IsOffline = true;
                    }
                }
                frame?.Navigate(typeof(PlayerPage), selection);
            };
            flyout.Items.Add(beginning);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        if (string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase))
        {
            var completedDownload = App.Downloads.GetCompletedDownload(item.Id);
            var isDownloading = App.Downloads.IsDownloading(item.Id);
            var download = new MenuFlyoutItem
            {
                Text = completedDownload is not null ? "Downloaded • open Downloads"
                     : isDownloading ? "Downloading… • open Downloads"
                     : "Download for offline playback"
            };
            download.Click += async (_, _) =>
            {
                if (completedDownload is not null || isDownloading)
                {
                    frame?.Navigate(typeof(DownloadsPage));
                    return;
                }

                try
                {
                    var dto = await App.Jellyfin.GetItemAsync(item.Id);
                    if (dto is not null)
                        await App.Downloads.DownloadAsync(dto);
                }
                catch { }
            };
            flyout.Items.Add(download);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var favorite = new MenuFlyoutItem { Text = item.IsFavorite ? "Remove from favourites" : "Add to favourites" };
        favorite.Click += async (_, _) =>
        {
            try
            {
                var next = !item.IsFavorite;
                await App.Jellyfin.MarkFavoriteAsync(item.Id, next);
                item.IsFavorite = next;
            }
            catch { }
        };
        flyout.Items.Add(favorite);

        var played = new MenuFlyoutItem { Text = item.Played ? "Mark as unplayed" : "Mark as played" };
        played.Click += async (_, _) =>
        {
            try
            {
                var next = !item.Played;
                await App.Jellyfin.MarkPlayedAsync(item.Id, next);
                item.Played = next;
                if (next) item.Progress = 100;
                else item.Progress = 0;
            }
            catch { }
        };
        flyout.Items.Add(played);

        flyout.ShowAt(target);
    }
}
