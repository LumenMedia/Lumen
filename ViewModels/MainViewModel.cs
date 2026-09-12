using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Services;

namespace Lumen.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly JellyfinClient _jellyfin;
    private readonly AppSettings _settings;

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "Home";
    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Not connected";
    [ObservableProperty]
    public partial string? UserName { get; set; }
    [ObservableProperty]
    public partial string UserInitial { get; set; } = "?";
    [ObservableProperty]
    public partial string UserImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial bool AutoLogin { get; set; } = true;
    [ObservableProperty]
    public partial bool AutoReconnect { get; set; } = true;
    [ObservableProperty]
    public partial bool PreferDirectPlay { get; set; } = true;
    [ObservableProperty]
    public partial bool HardwareAcceleration { get; set; } = true;
    [ObservableProperty]
    public partial bool AutoplayNextEpisode { get; set; } = true;
    [ObservableProperty]
    public partial bool ResumePlayback { get; set; } = true;
    [ObservableProperty]
    public partial bool AutoSkipIntro { get; set; }
    [ObservableProperty]
    public partial bool AutoSkipCredits { get; set; }
    [ObservableProperty]
    public partial bool ShowContinueWatching { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowUpNext { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowFavorites { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowRecentlyReleased { get; set; } = true;
    [ObservableProperty]
    public partial bool ShowRecentlyAdded { get; set; } = true;
    [ObservableProperty]
    public partial bool HeroTrailersEnabled { get; set; }
    [ObservableProperty]
    public partial int HomeLayoutIndex { get; set; }
    [ObservableProperty]
    public partial bool PreferSubtitles { get; set; }
    [ObservableProperty]
    public partial bool PreferForcedSubtitles { get; set; } = true;
    [ObservableProperty]
    public partial int SubtitleSizeIndex { get; set; } = 1;
    [ObservableProperty]
    public partial int SubtitleBackgroundIndex { get; set; } = 1;
    [ObservableProperty]
    public partial bool SubtitleShadow { get; set; } = true;
    [ObservableProperty]
    public partial bool GamepadEnabled { get; set; } = true;
    [ObservableProperty]
    public partial bool DiscordRichPresence { get; set; }
    [ObservableProperty]
    public partial bool MinimiseToTray { get; set; }
    [ObservableProperty]
    public partial bool DesktopNotifications { get; set; } = true;
    [ObservableProperty]
    public partial bool KeepPlayerOnTop { get; set; }
    [ObservableProperty]
    public partial bool PreferDownloadedPlayback { get; set; } = true;
    [ObservableProperty]
    public partial bool OfflineStartupEnabled { get; set; } = true;

    public ObservableCollection<LibraryViewModel> Libraries { get; } = [];

    [ObservableProperty]
    public partial string ServerName { get; set; } = "Jellyfin";
    [ObservableProperty]
    public partial string ServerVersion { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsAdministrator { get; set; }
    public string AppVersion => AppInfo.DisplayVersion;

    partial void OnUserNameChanged(string? value)
    {
        UserInitial = string.IsNullOrWhiteSpace(value)
            ? "?"
            : value.Trim()[0].ToString().ToUpperInvariant();
    }

    public MainViewModel(JellyfinClient jellyfin, AppSettings settings)
    {
        _jellyfin = jellyfin;
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        ServerUrl = _settings.ServerUrl;
        AutoLogin = _settings.AutoLogin;
        AutoReconnect = _settings.AutoReconnect;
        PreferDirectPlay = _settings.PreferDirectPlay;
        HardwareAcceleration = _settings.HardwareAcceleration;
        AutoplayNextEpisode = _settings.AutoplayNextEpisode;
        ResumePlayback = _settings.ResumePlayback;
        AutoSkipIntro = _settings.AutoSkipIntro;
        AutoSkipCredits = _settings.AutoSkipCredits;
        ShowContinueWatching = _settings.ShowContinueWatching;
        ShowUpNext = _settings.ShowUpNext;
        ShowFavorites = _settings.ShowFavorites;
        ShowRecentlyReleased = _settings.ShowRecentlyReleased;
        ShowRecentlyAdded = _settings.ShowRecentlyAdded;
        HeroTrailersEnabled = _settings.HeroTrailersEnabled;
        HomeLayoutIndex = _settings.HomeLayoutIndex;
        PreferSubtitles = _settings.PreferSubtitles;
        PreferForcedSubtitles = _settings.PreferForcedSubtitles;
        SubtitleSizeIndex = _settings.SubtitleSizeIndex;
        SubtitleBackgroundIndex = _settings.SubtitleBackgroundIndex;
        SubtitleShadow = _settings.SubtitleShadow;
        GamepadEnabled = _settings.GamepadEnabled;
        DiscordRichPresence = _settings.DiscordRichPresence;
        MinimiseToTray = _settings.MinimiseToTray;
        DesktopNotifications = _settings.DesktopNotifications;
        KeepPlayerOnTop = _settings.KeepPlayerOnTop;
        PreferDownloadedPlayback = _settings.PreferDownloadedPlayback;
        OfflineStartupEnabled = _settings.OfflineStartupEnabled;

        if (string.IsNullOrWhiteSpace(_settings.ServerUrl)) return;

        try
        {
            _jellyfin.Configure(_settings.ServerUrl);

            if (_settings.AutoLogin &&
                !string.IsNullOrWhiteSpace(_settings.UserId) &&
                !string.IsNullOrWhiteSpace(_settings.ProtectedAccessToken))
            {
                var token = SecureTokenStore.Unprotect(_settings.ProtectedAccessToken);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _jellyfin.RestoreSession(_settings.UserId, token);
                    try
                    {
                        var currentUser = await _jellyfin.GetUserAsync(_settings.UserId);
                        UserName = currentUser?.Name ?? _settings.Username;
                        IsAdministrator = currentUser?.Policy?.IsAdministrator == true;
                        UserImageUrl = currentUser is not null && !string.IsNullOrWhiteSpace(currentUser.Id)
                            ? _jellyfin.GetUserImageUrl(currentUser.Id!, currentUser.PrimaryImageTag, 96, 90)
                            : string.Empty;
                    }
                    catch
                    {
                        UserName = _settings.Username;
                        UserImageUrl = string.Empty;
                    }

                    try
                    {
                        var serverInfo = await _jellyfin.GetPublicSystemInfoAsync();
                        ServerName = string.IsNullOrWhiteSpace(serverInfo.ServerName) ? "Jellyfin" : serverInfo.ServerName;
                        ServerVersion = serverInfo.Version ?? string.Empty;
                    }
                    catch
                    {
                        ServerName = "Jellyfin";
                        ServerVersion = string.Empty;
                        IsAdministrator = false;
                    }

                    await LoadLibrariesAsync();
                    ConnectionStatus = "Connected";
                    return;
                }
            }

            var info = await _jellyfin.GetPublicSystemInfoAsync();
            ServerName = string.IsNullOrWhiteSpace(info.ServerName) ? "Jellyfin" : info.ServerName;
            ServerVersion = info.Version ?? string.Empty;
            ConnectionStatus = "Server found";
        }
        catch
        {
            _jellyfin.SignOut();
            ConnectionStatus = "Server unavailable";
        }
    }

    public async Task LoginAsync(string serverUrl, string username, string password, bool rememberLogin = true)
    {
        IsBusy = true;
        try
        {
            _jellyfin.Configure(serverUrl);
            var auth = await _jellyfin.AuthenticateAsync(username, password);

            _settings.ServerUrl = _jellyfin.BaseUrl;
            ServerUrl = _settings.ServerUrl;
            _settings.Username = username.Trim();
            _settings.AutoLogin = rememberLogin;
            AutoLogin = rememberLogin;

            if (rememberLogin && !string.IsNullOrWhiteSpace(_jellyfin.AccessToken) && !string.IsNullOrWhiteSpace(_jellyfin.UserId))
            {
                _settings.UserId = _jellyfin.UserId!;
                _settings.ProtectedAccessToken = SecureTokenStore.Protect(_jellyfin.AccessToken!);
                _settings.RememberUser(
                    _settings.ServerUrl,
                    auth.User?.Name ?? username.Trim(),
                    _settings.UserId,
                    _settings.ProtectedAccessToken);
            }
            else
            {
                _settings.ClearSession();
            }

            await _settings.SaveAsync();
            UserName = auth.User?.Name ?? username.Trim();
            IsAdministrator = auth.User?.Policy?.IsAdministrator == true;
            UserImageUrl = auth.User is not null && !string.IsNullOrWhiteSpace(auth.User.Id)
                ? _jellyfin.GetUserImageUrl(auth.User.Id!, auth.User.PrimaryImageTag, 96, 90)
                : string.Empty;

            try
            {
                var serverInfo = await _jellyfin.GetPublicSystemInfoAsync();
                ServerName = string.IsNullOrWhiteSpace(serverInfo.ServerName) ? "Jellyfin" : serverInfo.ServerName;
                ServerVersion = serverInfo.Version ?? string.Empty;
            }
            catch
            {
                ServerName = "Jellyfin";
                ServerVersion = string.Empty;
                IsAdministrator = false;
            }

            _settings.RememberServer(ServerName, _settings.ServerUrl);
            await _settings.SaveAsync();

            ConnectionStatus = "Connected";
            await LoadLibrariesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }


    public async Task<bool> SwitchToRememberedUserAsync(RememberedUserInfo account)
    {
        if (string.IsNullOrWhiteSpace(account.ServerUrl) ||
            string.IsNullOrWhiteSpace(account.UserId) ||
            string.IsNullOrWhiteSpace(account.ProtectedAccessToken))
            return false;

        // Validate before replacing the current session.
        try
        {
            var token = SecureTokenStore.Unprotect(account.ProtectedAccessToken);
            if (string.IsNullOrWhiteSpace(token))
                return false;

            _jellyfin.Configure(account.ServerUrl);
            _jellyfin.RestoreSession(account.UserId, token);
            var user = await _jellyfin.GetUserAsync(account.UserId);
            if (user?.Id is null)
                return false;

            _settings.ServerUrl = _jellyfin.BaseUrl;
            _settings.Username = user.Name ?? account.Username;
            _settings.UserId = account.UserId;
            _settings.ProtectedAccessToken = account.ProtectedAccessToken;
            _settings.AutoLogin = true;
            AutoLogin = true;
            ServerUrl = _settings.ServerUrl;
            UserName = user.Name ?? account.Username;
            IsAdministrator = user.Policy?.IsAdministrator == true;
            UserImageUrl = !string.IsNullOrWhiteSpace(user.Id)
                ? _jellyfin.GetUserImageUrl(user.Id!, user.PrimaryImageTag, 96, 90)
                : string.Empty;

            try
            {
                var serverInfo = await _jellyfin.GetPublicSystemInfoAsync();
                ServerName = string.IsNullOrWhiteSpace(serverInfo.ServerName) ? "Jellyfin" : serverInfo.ServerName;
                ServerVersion = serverInfo.Version ?? string.Empty;
            }
            catch
            {
                ServerName = "Jellyfin";
                ServerVersion = string.Empty;
            }

            Libraries.Clear();
            await LoadLibrariesAsync();
            ConnectionStatus = "Connected";
            _settings.RememberUser(_settings.ServerUrl, UserName ?? account.Username, account.UserId, account.ProtectedAccessToken);
            await _settings.SaveAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryReconnectAsync(CancellationToken ct = default)
    {
        if (!AutoReconnect || string.IsNullOrWhiteSpace(_settings.ServerUrl))
            return false;

        try
        {
            ConnectionStatus = "Reconnecting…";
            _jellyfin.Configure(_settings.ServerUrl);

            if (!_jellyfin.IsAuthenticated &&
                !string.IsNullOrWhiteSpace(_settings.UserId) &&
                !string.IsNullOrWhiteSpace(_settings.ProtectedAccessToken))
            {
                var token = SecureTokenStore.Unprotect(_settings.ProtectedAccessToken);
                if (!string.IsNullOrWhiteSpace(token))
                    _jellyfin.RestoreSession(_settings.UserId, token);
            }

            var info = await _jellyfin.GetPublicSystemInfoAsync(ct);
            ServerName = string.IsNullOrWhiteSpace(info.ServerName) ? "Jellyfin" : info.ServerName;
            ServerVersion = info.Version ?? string.Empty;

            if (_jellyfin.IsAuthenticated)
            {
                if (Libraries.Count == 0)
                    await LoadLibrariesAsync();
                ConnectionStatus = "Connected";
                return true;
            }

            ConnectionStatus = "Server found";
            return false;
        }
        catch
        {
            ConnectionStatus = "Server unavailable • retrying";
            return false;
        }
    }

    public async Task LoadLibrariesAsync()
    {
        if (!_jellyfin.IsAuthenticated) return;
        IsBusy = true;
        try
        {
            var views = await _jellyfin.GetUserViewsAsync();
            Libraries.Clear();
            foreach (var view in views.Items)
            {
                if (string.IsNullOrWhiteSpace(view.Id) || string.IsNullOrWhiteSpace(view.Name)) continue;
                Libraries.Add(LibraryViewModel.FromDto(view));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveSettingsAsync()
    {
        _settings.AutoLogin = AutoLogin;
        _settings.AutoReconnect = AutoReconnect;
        _settings.PreferDirectPlay = PreferDirectPlay;
        _settings.HardwareAcceleration = HardwareAcceleration;
        _settings.AutoplayNextEpisode = AutoplayNextEpisode;
        _settings.ResumePlayback = ResumePlayback;
        _settings.AutoSkipIntro = AutoSkipIntro;
        _settings.AutoSkipCredits = AutoSkipCredits;
        _settings.ShowContinueWatching = ShowContinueWatching;
        _settings.ShowUpNext = ShowUpNext;
        _settings.ShowFavorites = ShowFavorites;
        _settings.ShowRecentlyReleased = ShowRecentlyReleased;
        _settings.ShowRecentlyAdded = ShowRecentlyAdded;
        _settings.HeroTrailersEnabled = HeroTrailersEnabled;
        _settings.HomeLayoutIndex = HomeLayoutIndex;
        _settings.PreferSubtitles = PreferSubtitles;
        _settings.PreferForcedSubtitles = PreferForcedSubtitles;
        _settings.SubtitleSizeIndex = SubtitleSizeIndex;
        _settings.SubtitleBackgroundIndex = SubtitleBackgroundIndex;
        _settings.SubtitleShadow = SubtitleShadow;
        _settings.GamepadEnabled = GamepadEnabled;
        _settings.DiscordRichPresence = DiscordRichPresence;
        _settings.MinimiseToTray = MinimiseToTray;
        _settings.DesktopNotifications = DesktopNotifications;
        _settings.KeepPlayerOnTop = KeepPlayerOnTop;
        _settings.PreferDownloadedPlayback = PreferDownloadedPlayback;
        _settings.OfflineStartupEnabled = OfflineStartupEnabled;
        await _settings.SaveAsync();
    }

    public void ClearArtworkCache()
    {
        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen", "Cache");
        try
        {
            if (Directory.Exists(cacheFolder))
                Directory.Delete(cacheFolder, recursive: true);
        }
        catch { }
    }

    public string GetDiagnosticsText()
    {
        var downloads = App.Downloads.Items.Count(x => x.IsComplete && File.Exists(x.LocalPath));
        return $"Lumen {AppVersion}\n" +
               $"Server: {_settings.ServerUrl}\n" +
               $"User: {UserName}\n" +
               $"Connection: {ConnectionStatus}\n" +
               $"Libraries: {Libraries.Count}\n" +
               $"Offline downloads: {downloads}\n" +
               $"Prefer downloaded playback: {PreferDownloadedPlayback}\n" +
               $"Offline startup: {OfflineStartupEnabled}";
    }

    public async Task ForgetSessionAsync(bool clearServer = false)
    {
        _jellyfin.SignOut();
        _settings.ClearSession();
        if (clearServer)
        {
            _settings.ServerUrl = string.Empty;
            _settings.Username = string.Empty;
        }
        await _settings.SaveAsync();
        Libraries.Clear();
        UserName = null;
        UserImageUrl = string.Empty;
        ConnectionStatus = "Not connected";
        ServerName = "Jellyfin";
        ServerVersion = string.Empty;
        IsAdministrator = false;
    }
}
