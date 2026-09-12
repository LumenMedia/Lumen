using System.Text.Json;

namespace Lumen.Services;

public sealed class RememberedServerInfo
{
    public string Name { get; set; } = "Jellyfin";
    public string Address { get; set; } = string.Empty;
}

public sealed class RememberedUserInfo
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProtectedAccessToken { get; set; } = string.Empty;
}

public sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public bool AutoLogin { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public List<RememberedServerInfo> RememberedServers { get; set; } = [];
    public List<RememberedUserInfo> RememberedUsers { get; set; } = [];
    public bool PreferDirectPlay { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool AutoplayNextEpisode { get; set; } = true;
    public bool ResumePlayback { get; set; } = true;
    public bool AutoSkipIntro { get; set; } = false;
    public bool AutoSkipCredits { get; set; } = false;
    public bool ShowContinueWatching { get; set; } = true;
    public bool ShowUpNext { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public bool ShowRecentlyReleased { get; set; } = true;
    public bool ShowRecentlyAdded { get; set; } = true;
    public bool HeroTrailersEnabled { get; set; } = false;
    public int HomeLayoutIndex { get; set; } = 0;
    public bool PreferSubtitles { get; set; } = false;
    public bool PreferForcedSubtitles { get; set; } = true;
    public int SubtitleSizeIndex { get; set; } = 1;
    public int SubtitleBackgroundIndex { get; set; } = 1;
    public bool SubtitleShadow { get; set; } = true;
    public bool GamepadEnabled { get; set; } = true;
    public bool DiscordRichPresence { get; set; } = false;
    public bool MinimiseToTray { get; set; } = false;
    public bool DesktopNotifications { get; set; } = true;
    public bool KeepPlayerOnTop { get; set; } = false;
    public int DownloadConcurrency { get; set; } = 2;
    public string DownloadFolder { get; set; } = string.Empty;
    public string DownloadQuality { get; set; } = "Original";
    public bool PreferDownloadedPlayback { get; set; } = true;
    public bool OfflineStartupEnabled { get; set; } = true;
    public Dictionary<string, double> LyricOffsets { get; set; } = [];
    public Dictionary<string, double> LyricScales { get; set; } = [];

    public async Task LoadAsync()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            var saved = JsonSerializer.Deserialize<AppSettings>(json);
            if (saved is null) return;
            ServerUrl = saved.ServerUrl;
            Username = saved.Username;
            UserId = saved.UserId;
            ProtectedAccessToken = saved.ProtectedAccessToken;
            AutoLogin = saved.AutoLogin;
            AutoReconnect = saved.AutoReconnect;
            RememberedServers = saved.RememberedServers ?? [];
            RememberedUsers = saved.RememberedUsers ?? [];

            // Migrate the original single remembered login into the account picker.
            if (RememberedUsers.Count == 0 &&
                !string.IsNullOrWhiteSpace(saved.ServerUrl) &&
                !string.IsNullOrWhiteSpace(saved.Username) &&
                !string.IsNullOrWhiteSpace(saved.UserId) &&
                !string.IsNullOrWhiteSpace(saved.ProtectedAccessToken))
            {
                RememberedUsers.Add(new RememberedUserInfo
                {
                    ServerUrl = saved.ServerUrl,
                    Username = saved.Username,
                    UserId = saved.UserId,
                    ProtectedAccessToken = saved.ProtectedAccessToken
                });
            }

            PreferDirectPlay = saved.PreferDirectPlay;
            HardwareAcceleration = saved.HardwareAcceleration;
            AutoplayNextEpisode = saved.AutoplayNextEpisode;
            ResumePlayback = saved.ResumePlayback;
            AutoSkipIntro = saved.AutoSkipIntro;
            AutoSkipCredits = saved.AutoSkipCredits;
            ShowContinueWatching = saved.ShowContinueWatching;
            ShowUpNext = saved.ShowUpNext;
            ShowFavorites = saved.ShowFavorites;
            ShowRecentlyReleased = saved.ShowRecentlyReleased;
            ShowRecentlyAdded = saved.ShowRecentlyAdded;
            HeroTrailersEnabled = saved.HeroTrailersEnabled;
            HomeLayoutIndex = saved.HomeLayoutIndex;
            PreferSubtitles = saved.PreferSubtitles;
            PreferForcedSubtitles = saved.PreferForcedSubtitles;
            SubtitleSizeIndex = saved.SubtitleSizeIndex;
            SubtitleBackgroundIndex = saved.SubtitleBackgroundIndex;
            SubtitleShadow = saved.SubtitleShadow;
            GamepadEnabled = saved.GamepadEnabled;
            DiscordRichPresence = saved.DiscordRichPresence;
            MinimiseToTray = saved.MinimiseToTray;
            DesktopNotifications = saved.DesktopNotifications;
            KeepPlayerOnTop = saved.KeepPlayerOnTop;
            DownloadConcurrency = Math.Clamp(saved.DownloadConcurrency <= 0 ? 2 : saved.DownloadConcurrency, 1, 8);
            DownloadFolder = saved.DownloadFolder ?? string.Empty;
            DownloadQuality = string.IsNullOrWhiteSpace(saved.DownloadQuality) ? "Original" : saved.DownloadQuality;
            PreferDownloadedPlayback = saved.PreferDownloadedPlayback;
            OfflineStartupEnabled = saved.OfflineStartupEnabled;
            LyricOffsets = saved.LyricOffsets ?? [];
            LyricScales = saved.LyricScales ?? [];
        }
        catch { }
    }

    public void RememberServer(string name, string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        address = address.Trim().TrimEnd('/');
        RememberedServers.RemoveAll(x => string.Equals(x.Address, address, StringComparison.OrdinalIgnoreCase));
        RememberedServers.Insert(0, new RememberedServerInfo
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Jellyfin" : name.Trim(),
            Address = address
        });
        if (RememberedServers.Count > 5)
            RememberedServers.RemoveRange(5, RememberedServers.Count - 5);
    }

    public void RememberUser(string serverUrl, string username, string userId, string protectedAccessToken)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(protectedAccessToken))
            return;

        serverUrl = serverUrl.Trim().TrimEnd('/');
        RememberedUsers.RemoveAll(x =>
            string.Equals(x.ServerUrl.Trim().TrimEnd('/'), serverUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));

        RememberedUsers.Insert(0, new RememberedUserInfo
        {
            ServerUrl = serverUrl,
            Username = username.Trim(),
            UserId = userId,
            ProtectedAccessToken = protectedAccessToken
        });

        if (RememberedUsers.Count > 12)
            RememberedUsers.RemoveRange(12, RememberedUsers.Count - 12);
    }

    public RememberedUserInfo? FindRememberedUser(string serverUrl, string userId)
    {
        serverUrl = (serverUrl ?? string.Empty).Trim().TrimEnd('/');
        return RememberedUsers.FirstOrDefault(x =>
            string.Equals(x.ServerUrl.Trim().TrimEnd('/'), serverUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(FilePath, json);
    }

    public void ClearSession()
    {
        UserId = string.Empty;
        ProtectedAccessToken = string.Empty;
    }
}
