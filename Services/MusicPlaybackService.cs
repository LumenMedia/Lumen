using Lumen.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Lumen.Services;

public enum MusicRepeatMode { Off, All, One }

public sealed class MusicPlaybackService : IDisposable
{
    public MediaPlayer Player { get; } = new();
    public event EventHandler? Changed;
    public BaseItemDto? CurrentItem { get; private set; }
    public List<BaseItemDto> Queue { get; private set; } = [];
    public int QueueIndex { get; private set; } = -1;
    public bool ShuffleEnabled { get; private set; }
    public MusicRepeatMode RepeatMode { get; private set; }
    public string ArtworkUrl { get; private set; } = string.Empty;
    private readonly Random _random = new();
    private bool _disposed;

    public MusicPlaybackService()
    {
        Player.CommandManager.IsEnabled = true;
        Player.MediaEnded += Player_MediaEnded;
        Player.PlaybackSession.PlaybackStateChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool HasTrack => CurrentItem is not null;
    public bool IsPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public async Task PlayItemAsync(string id, bool rebuildQueue = true)
    {
        if (_disposed) return;
        var item = await App.Jellyfin.GetItemAsync(id);
        if (item?.Id is null) return;

        CurrentItem = item;
        ArtworkUrl = App.Jellyfin.GetImageUrl(item.AlbumId ?? item.Id, "Primary", item.PrimaryImageTag, 700, 92);

        if (rebuildQueue)
        {
            if (!string.IsNullOrWhiteSpace(item.AlbumId))
            {
                var tracks = await App.Jellyfin.GetAlbumTracksAsync(item.AlbumId);
                Queue = tracks.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
                QueueIndex = Queue.FindIndex(x => x.Id == item.Id);
            }
            else { Queue = [item]; QueueIndex = 0; }
        }
        else if (QueueIndex < 0)
            QueueIndex = Queue.FindIndex(x => x.Id == item.Id);

        Player.Source = MediaSource.CreateFromUri(new Uri(App.Jellyfin.GetDirectPlayUrl(item.Id)));
        Player.Play();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PlayQueueAsync(
        IEnumerable<BaseItemDto> items,
        int startIndex = 0,
        bool shuffle = false)
    {
        if (_disposed) return;

        Queue = items.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
        if (Queue.Count == 0) return;

        ShuffleEnabled = shuffle;
        QueueIndex = Math.Clamp(startIndex, 0, Queue.Count - 1);
        if (shuffle && Queue.Count > 1)
            QueueIndex = _random.Next(Queue.Count);

        var id = Queue[QueueIndex].Id;
        if (!string.IsNullOrWhiteSpace(id))
            await PlayItemAsync(id!, rebuildQueue: false);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PlayPause()
    {
        if (IsPlaying) Player.Pause(); else Player.Play();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        Player.PlaybackSession.Position = position;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PlayQueueIndexAsync(int index)
    {
        if (_disposed || Queue.Count == 0 || index < 0 || index >= Queue.Count) return;
        QueueIndex = index;
        if (Queue[QueueIndex].Id is string id)
            await PlayItemAsync(id, rebuildQueue: false);
    }

    public async Task PreviousAsync()
    {
        if (Queue.Count == 0) return;
        if (Player.PlaybackSession.Position.TotalSeconds > 3) { Seek(TimeSpan.Zero); return; }
        QueueIndex = ShuffleEnabled && Queue.Count > 1 ? RandomOtherIndex() : Math.Max(0, QueueIndex - 1);
        if (Queue[QueueIndex].Id is string id) await PlayItemAsync(id, false);
    }

    public async Task NextAsync(bool naturalEnd = false)
    {
        if (Queue.Count == 0) return;
        if (RepeatMode == MusicRepeatMode.One && QueueIndex >= 0)
        {
            if (Queue[QueueIndex].Id is string same) await PlayItemAsync(same, false);
            return;
        }
        if (ShuffleEnabled && Queue.Count > 1) QueueIndex = RandomOtherIndex();
        else if (QueueIndex + 1 < Queue.Count) QueueIndex++;
        else if (RepeatMode == MusicRepeatMode.All) QueueIndex = 0;
        else { if (naturalEnd) Changed?.Invoke(this, EventArgs.Empty); return; }
        if (Queue[QueueIndex].Id is string id) await PlayItemAsync(id, false);
    }

    public void ToggleShuffle() { ShuffleEnabled = !ShuffleEnabled; Changed?.Invoke(this, EventArgs.Empty); }
    public void CycleRepeat()
    {
        RepeatMode = RepeatMode switch { MusicRepeatMode.Off => MusicRepeatMode.All, MusicRepeatMode.All => MusicRepeatMode.One, _ => MusicRepeatMode.Off };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int RandomOtherIndex()
    {
        if (Queue.Count <= 1) return Math.Max(0, QueueIndex);
        var next = QueueIndex;
        while (next == QueueIndex) next = _random.Next(Queue.Count);
        return next;
    }

    private async void Player_MediaEnded(MediaPlayer sender, object args)
    {
        try { await NextAsync(true); } catch { }
    }

    public void PauseForVideo() { if (IsPlaying) Player.Pause(); Changed?.Invoke(this, EventArgs.Empty); }

    public void StopAndClear()
    {
        if (_disposed) return;
        try { Player.Pause(); Player.Source = null; } catch { }
        CurrentItem = null;
        ArtworkUrl = string.Empty;
        Queue = [];
        QueueIndex = -1;
        ShuffleEnabled = false;
        RepeatMode = MusicRepeatMode.Off;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        try { Player.CommandManager.IsEnabled = false; } catch { }
        try { Player.MediaEnded -= Player_MediaEnded; } catch { }
        try { Player.Pause(); Player.Source = null; } catch { }
    }
    public void Dispose() { Shutdown(); Player.Dispose(); }
}
