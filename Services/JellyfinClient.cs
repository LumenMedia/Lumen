using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lumen.Models;
using Windows.Media.Core;

namespace Lumen.Services;

public sealed class JellyfinClient : IDisposable
{
    private const string ClientName = "Lumen";
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

    private string? _token;
    private string? _userId;
    private readonly string _deviceId;
    private string _baseUrl = string.Empty;

    public string BaseUrl => _baseUrl;
    public string? UserId => _userId;
    public string? AccessToken => _token;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_token) && !string.IsNullOrWhiteSpace(_userId);

    public JellyfinClient()
    {
        _deviceId = GetDeviceId();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Configure(string serverUrl)
    {
        var normalized = NormalizeServerUrl(serverUrl);
        if (!string.Equals(_baseUrl, normalized, StringComparison.OrdinalIgnoreCase))
        {
            _token = null;
            _userId = null;
        }
        _baseUrl = normalized;
    }

    public void RestoreSession(string userId, string accessToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("A valid Jellyfin user ID and access token are required.");
        _userId = userId;
        _token = accessToken;
    }

    public async Task<PublicSystemInfo> GetPublicSystemInfoAsync(CancellationToken ct = default)
        => await SendAsync<PublicSystemInfo>(HttpMethod.Get, "/System/Info/Public", null, false, ct)
           ?? throw new InvalidOperationException("Jellyfin returned no server information.");

    public async Task<UserDto?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<UserDto>(HttpMethod.Get, $"/Users/{Esc(userId)}", null, true, ct);
    }

    public async Task<AuthenticateResponse> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        EnsureConfigured();
        var body = new { Username = username, Pw = password };
        var response = await SendAsync<AuthenticateResponse>(HttpMethod.Post, "/Users/AuthenticateByName", body, false, ct)
            ?? throw new InvalidOperationException("Jellyfin returned an empty authentication response.");

        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.User?.Id))
            throw new InvalidOperationException("Jellyfin authentication succeeded without a usable token or user ID.");

        _token = response.AccessToken;
        _userId = response.User.Id;
        return response;
    }

    public async Task<QueryResult<BaseItemDto>> GetUserViewsAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<QueryResult<BaseItemDto>>(
            HttpMethod.Get,
            $"/UserViews?userId={Esc(_userId!)}&includeHidden=false",
            null, true, ct) ?? new QueryResult<BaseItemDto>();
    }

    public async Task<QueryResult<BaseItemDto>> GetItemsAsync(
        string? parentId = null,
        string? includeItemTypes = null,
        bool recursive = true,
        string sortBy = "SortName",
        string sortOrder = "Ascending",
        int limit = 100,
        int startIndex = 0,
        bool enableUserData = true,
        string? filters = null,
        string? mediaTypes = null,
        string? fields = null,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var query = new List<string>
        {
            $"userId={Esc(_userId!)}",
            $"recursive={recursive}",
            $"sortBy={Esc(sortBy)}",
            $"sortOrder={Esc(sortOrder)}",
            $"limit={limit}",
            $"startIndex={startIndex}",
            $"enableUserData={enableUserData}"
        };
        if (!string.IsNullOrWhiteSpace(filters)) query.Add($"filters={Esc(filters)}");
        if (!string.IsNullOrWhiteSpace(mediaTypes)) query.Add($"mediaTypes={Esc(mediaTypes)}");
        if (!string.IsNullOrWhiteSpace(parentId)) query.Add($"parentId={Esc(parentId)}");
        if (!string.IsNullOrWhiteSpace(includeItemTypes)) query.Add($"includeItemTypes={Esc(includeItemTypes)}");
        if (!string.IsNullOrWhiteSpace(fields)) query.Add($"fields={Esc(fields)}");

        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, $"/Items?{string.Join("&", query)}", null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetLibraryItemsAsync(
        string libraryId,
        int limit = 100,
        string sortBy = "SortName",
        string sortOrder = "Ascending",
        CancellationToken ct = default)
        => GetItemsAsync(libraryId, recursive: true, sortBy: sortBy, sortOrder: sortOrder, limit: limit,
            enableUserData: true, fields: "Genres,MediaStreams,DateCreated,Overview", ct: ct);

    public Task<QueryResult<BaseItemDto>> GetLatestMoviesAsync(int limit = 20, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Movie", recursive: true,
            sortBy: "DateCreated", sortOrder: "Descending", limit: limit, enableUserData: true, ct: ct);

    public Task<QueryResult<BaseItemDto>> GetLatestSeriesAsync(int limit = 20, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Series", recursive: true,
            sortBy: "DateCreated", sortOrder: "Descending", limit: limit, enableUserData: true, ct: ct);

    public Task<QueryResult<BaseItemDto>> GetRecentlyReleasedMoviesAsync(int limit = 20, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Movie", recursive: true,
            sortBy: "PremiereDate", sortOrder: "Descending", limit: limit, enableUserData: true, ct: ct);

    public Task<QueryResult<BaseItemDto>> GetRecentlyReleasedSeriesAsync(int limit = 20, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Series", recursive: true,
            sortBy: "PremiereDate", sortOrder: "Descending", limit: limit, enableUserData: true, ct: ct);

    public async Task<QueryResult<BaseItemDto>> GetUpNextAsync(int limit = 20, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Shows/NextUp" +
                   $"?userId={Esc(_userId!)}" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&imageTypeLimit=1" +
                   "&enableImages=true" +
                   "&enableTotalRecordCount=false" +
                   "&disableFirstEpisode=false" +
                   "&enableResumable=false" +
                   "&enableRewatching=false";

        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetLatestItemsAsync(int limit = 20, CancellationToken ct = default)
        => GetLatestMoviesAsync(limit, ct);

    public async Task<QueryResult<BaseItemDto>> GetContinueWatchingAsync(int limit = 20, CancellationToken ct = default)
    {
        EnsureAuthenticated();

        // Use Jellyfin's dedicated Resume endpoint for Continue Watching.
        var path = "/UserItems/Resume" +
                   $"?limit={Math.Max(1, limit)}" +
                   "&mediaTypes=Video" +
                   "&includeItemTypes=Episode" +
                   "&includeItemTypes=Movie" +
                   "&enableImages=true" +
                   "&enableTotalRecordCount=false" +
                   "&excludeActiveSessions=false" +
                   "&imageTypeLimit=1";

        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> SearchItemsAsync(string query, int limit = 60, CancellationToken ct = default)
        => SearchCoreAsync(query, limit, ct);

    private async Task<QueryResult<BaseItemDto>> SearchCoreAsync(string query, int limit, CancellationToken ct)
    {
        EnsureAuthenticated();
        var path = $"/Items?userId={Esc(_userId!)}&searchTerm={Esc(query)}&recursive=true&sortBy=SortName&sortOrder=Ascending&limit={limit}&startIndex=0&enableUserData=true&includeItemTypes=Movie,Series,Season,Episode,Person,BoxSet,MusicAlbum,MusicArtist,Audio";
        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct) ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> SearchAsync(string query, int limit = 60, CancellationToken ct = default)
        => SearchCoreAsync(query, limit, ct);

    public Task<QueryResult<BaseItemDto>> GetMusicItemsAsync(string libraryId, string kind, int limit = 500, CancellationToken ct = default)
    {
        if (string.Equals(kind, "Artists", StringComparison.OrdinalIgnoreCase))
            return GetMusicArtistsAsync(libraryId, limit, ct);

        var include = string.Equals(kind, "Songs", StringComparison.OrdinalIgnoreCase)
            ? "Audio"
            : "MusicAlbum";

        return GetItemsAsync(libraryId, includeItemTypes: include, recursive: true,
            sortBy: "SortName", sortOrder: "Ascending", limit: limit, enableUserData: true,
            fields: "Genres,DateCreated,Overview,Album,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);
    }

    public async Task<QueryResult<BaseItemDto>> GetMusicArtistsAsync(
        string libraryId,
        int limit = 500,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Artists" +
                   $"?userId={Esc(_userId!)}" +
                   $"&parentId={Esc(libraryId)}" +
                   "&sortBy=SortName" +
                   "&sortOrder=Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableImages=true" +
                   "&enableUserData=true" +
                   "&enableTotalRecordCount=false" +
                   "&fields=Genres,DateCreated,Overview";

        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetMusicRecentlyAddedAlbumsAsync(
        string libraryId, int limit = 24, CancellationToken ct = default)
        => GetItemsAsync(libraryId, includeItemTypes: "MusicAlbum", recursive: true,
            sortBy: "DateCreated", sortOrder: "Descending", limit: limit, enableUserData: true,
            fields: "Genres,DateCreated,Overview,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);

    public Task<QueryResult<BaseItemDto>> GetMusicRecentlyPlayedAsync(
        string libraryId, int limit = 20, CancellationToken ct = default)
        => GetItemsAsync(libraryId, includeItemTypes: "Audio", recursive: true,
            sortBy: "DatePlayed", sortOrder: "Descending", limit: limit, enableUserData: true,
            fields: "Genres,DateCreated,Album,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);

    public Task<QueryResult<BaseItemDto>> GetFavoriteMusicAlbumsAsync(
        string libraryId, int limit = 24, CancellationToken ct = default)
        => GetItemsAsync(libraryId, includeItemTypes: "MusicAlbum", recursive: true,
            sortBy: "SortName", sortOrder: "Ascending", limit: limit, enableUserData: true,
            filters: "IsFavorite",
            fields: "Genres,DateCreated,Overview,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);

    public Task<QueryResult<BaseItemDto>> GetFavoriteMusicSongsAsync(
        string libraryId, int limit = 200, CancellationToken ct = default)
        => GetItemsAsync(libraryId, includeItemTypes: "Audio", recursive: true,
            sortBy: "SortName", sortOrder: "Ascending", limit: limit, enableUserData: true,
            filters: "IsFavorite",
            fields: "Genres,DateCreated,Album,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);

    public Task<QueryResult<BaseItemDto>> GetMusicPlaylistsAsync(
        int limit = 200, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Playlist", recursive: true,
            sortBy: "SortName", sortOrder: "Ascending", limit: limit, enableUserData: true,
            fields: "Genres,DateCreated,Overview", ct: ct);

    public async Task<QueryResult<BaseItemDto>> GetMusicPlaylistTracksAsync(
        string playlistId, int limit = 2000, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&parentId={Esc(playlistId)}" +
                   "&recursive=true" +
                   "&includeItemTypes=Audio" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&fields=Album,AlbumArtist,Artists,ArtistItems,AlbumArtists";
        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public async Task<QueryResult<BaseItemDto>> SearchMusicAsync(
        string libraryId, string query, int limit = 120, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(query))
            return new QueryResult<BaseItemDto>();

        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&parentId={Esc(libraryId)}" +
                   $"&searchTerm={Esc(query.Trim())}" +
                   "&recursive=true" +
                   "&sortBy=SortName" +
                   "&sortOrder=Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&includeItemTypes=MusicAlbum,MusicArtist,Audio" +
                   "&fields=Genres,DateCreated,Overview,Album,AlbumArtist,Artists,ArtistItems,AlbumArtists";

        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetAlbumTracksAsync(string albumId, CancellationToken ct = default)
        => GetItemsAsync(albumId, includeItemTypes: "Audio", recursive: true,
            sortBy: "ParentIndexNumber,IndexNumber,SortName", sortOrder: "Ascending",
            limit: 500, enableUserData: true,
            fields: "Album,AlbumArtist,Artists,ArtistItems,AlbumArtists", ct: ct);

    public async Task<QueryResult<BaseItemDto>> GetArtistAlbumsAsync(
        string artistId,
        int limit = 300,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&artistIds={Esc(artistId)}" +
                   "&recursive=true" +
                   "&includeItemTypes=MusicAlbum" +
                   "&sortBy=ProductionYear,SortName" +
                   "&sortOrder=Descending,Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&fields=Overview,Genres,AlbumArtist,Artists,ArtistItems,AlbumArtists,DateCreated";
        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public async Task<QueryResult<BaseItemDto>> GetArtistSongsAsync(
        string artistId,
        int limit = 1000,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&artistIds={Esc(artistId)}" +
                   "&recursive=true" +
                   "&includeItemTypes=Audio" +
                   "&sortBy=SortName" +
                   "&sortOrder=Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&fields=Album,AlbumId,AlbumArtist,Artists,ArtistItems,AlbumArtists,Overview,DateCreated";
        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public async Task<LyricsResultDto?> GetLyricsAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        try
        {
            return await SendAsync<LyricsResultDto>(HttpMethod.Get, $"/Audio/{Esc(itemId)}/Lyrics", null, true, ct);
        }
        catch { return null; }
    }

    public async Task<LyricsResultDto?> UploadLyricsAsync(string itemId, string fileName, string content, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_baseUrl}/Audio/{Esc(itemId)}/Lyrics?fileName={Esc(fileName)}");
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(includeToken: true));
        request.Content = new StringContent(content, System.Text.Encoding.UTF8, "text/plain");
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<LyricsResultDto>(json);
    }

    public async Task DeleteLyricsAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Delete, $"/Audio/{Esc(itemId)}/Lyrics", null, true, ct);
    }

    public async Task<List<RemoteLyricInfoDto>> SearchRemoteLyricsAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        try
        {
            return await SendAsync<List<RemoteLyricInfoDto>>(
                HttpMethod.Get, $"/Audio/{Esc(itemId)}/RemoteSearch/Lyrics", null, true, ct) ?? [];
        }
        catch { return []; }
    }

    public Task<LyricsResultDto?> DownloadRemoteLyricsAsync(string itemId, string lyricId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return SendAsync<LyricsResultDto>(
            HttpMethod.Post, $"/Audio/{Esc(itemId)}/RemoteSearch/Lyrics/{Esc(lyricId)}", null, true, ct);
    }


    public async Task<BaseItemDto?> GetItemAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        const string fields = "People,MediaSources,MediaStreams,Genres,Overview,Chapters,ProviderIds,Studios,Taglines,ProductionLocations,DateCreated,Artists,ArtistItems,AlbumArtists,Album,AlbumArtist";
        return await SendAsync<BaseItemDto>(HttpMethod.Get,
            $"/Users/{Esc(_userId!)}/Items/{Esc(itemId)}?fields={Esc(fields)}", null, true, ct);
    }

    public Task<QueryResult<BaseItemDto>> GetFavoriteItemsAsync(int limit = 40, CancellationToken ct = default)
        => GetItemsAsync(includeItemTypes: "Movie,Series", recursive: true,
            sortBy: "DateCreated", sortOrder: "Descending", limit: limit,
            enableUserData: true, filters: "IsFavorite", ct: ct);

    public async Task<QueryResult<BaseItemDto>> GetItemsByGenreAsync(string genre, int limit = 200, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&genres={Esc(genre)}" +
                   "&recursive=true" +
                   "&includeItemTypes=Movie,Series" +
                   "&sortBy=SortName" +
                   "&sortOrder=Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&fields=Genres,Overview,DateCreated";
        return await SendAsync<QueryResult<BaseItemDto>>(HttpMethod.Get, path, null, true, ct)
               ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetCollectionItemsAsync(string collectionId, CancellationToken ct = default)
        => GetItemsAsync(parentId: collectionId, includeItemTypes: "Movie,Series,BoxSet", recursive: false,
            sortBy: "ProductionYear,SortName", sortOrder: "Ascending", limit: 300,
            enableUserData: true, fields: "Overview,Genres,DateCreated", ct: ct);

    public async Task<List<BaseItemDto>> GetSpecialFeaturesAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<List<BaseItemDto>>(
            HttpMethod.Get,
            $"/Items/{Esc(itemId)}/SpecialFeatures?userId={Esc(_userId!)}",
            null, true, ct) ?? [];
    }

    public async Task<List<UserDto>> GetPublicUsersAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        return await SendAsync<List<UserDto>>(HttpMethod.Get, "/Users/Public", null, false, ct) ?? [];
    }

    public async Task<QueryResult<BaseItemDto>> GetItemsByPersonAsync(string personId, int limit = 200, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var path = "/Items" +
                   $"?userId={Esc(_userId!)}" +
                   $"&personIds={Esc(personId)}" +
                   "&recursive=true" +
                   "&includeItemTypes=Movie,Series" +
                   "&sortBy=ProductionYear,SortName" +
                   "&sortOrder=Descending,Ascending" +
                   $"&limit={Math.Max(1, limit)}" +
                   "&enableUserData=true" +
                   "&fields=Overview,Genres,People";

        return await SendAsync<QueryResult<BaseItemDto>>(
            HttpMethod.Get, path, null, true, ct) ?? new QueryResult<BaseItemDto>();
    }

    public async Task<QueryResult<BaseItemDto>> GetSimilarItemsAsync(string itemId, int limit = 12, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<QueryResult<BaseItemDto>>(
            HttpMethod.Get,
            $"/Items/{Esc(itemId)}/Similar?userId={Esc(_userId!)}&limit={Math.Max(1, limit)}&fields=Overview,Genres",
            null, true, ct) ?? new QueryResult<BaseItemDto>();
    }

    public Task<QueryResult<BaseItemDto>> GetSeasonsAsync(string seriesId, CancellationToken ct = default)
        => GetItemsAsync(parentId: seriesId, includeItemTypes: "Season", recursive: false,
            sortBy: "SortName", sortOrder: "Ascending", limit: 100, enableUserData: true, ct: ct);

    public Task<QueryResult<BaseItemDto>> GetEpisodesAsync(string seasonId, CancellationToken ct = default)
        => GetItemsAsync(parentId: seasonId, includeItemTypes: "Episode", recursive: false,
            sortBy: "SortName", sortOrder: "Ascending", limit: 200, enableUserData: true, ct: ct);

    public async Task<QueryResult<MediaSegmentDto>> GetMediaSegmentsAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<QueryResult<MediaSegmentDto>>(
            HttpMethod.Get,
            $"/MediaSegments/{Esc(itemId)}",
            null, true, ct) ?? new QueryResult<MediaSegmentDto>();
    }

    public async Task MarkFavoriteAsync(string itemId, bool favorite, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var method = favorite ? HttpMethod.Post : HttpMethod.Delete;
        await SendAsync<object>(method,
            $"/Users/{Esc(_userId!)}/FavoriteItems/{Esc(itemId)}", null, true, ct);
    }

    public async Task MarkPlayedAsync(string itemId, bool played, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var method = played ? HttpMethod.Post : HttpMethod.Delete;
        await SendAsync<object>(method,
            $"/Users/{Esc(_userId!)}/PlayedItems/{Esc(itemId)}", null, true, ct);
    }

    private async Task<object> BuildNativeDeviceProfileAsync()
    {
        // Build the playback profile from codecs installed on this PC.
        var query = new CodecQuery();

        async Task<bool> HasDecoderAsync(CodecKind kind, string subtype)
        {
            try
            {
                var matches = await query.FindAllAsync(kind, CodecCategory.Decoder, subtype);
                return matches.Count > 0;
            }
            catch
            {
                // Unconfirmed codec support should fall back to transcoding.
                return false;
            }
        }

        var audioCodecs = new List<string>();
        async Task AddAudioAsync(string jellyfinName, string subtype)
        {
            if (await HasDecoderAsync(CodecKind.Audio, subtype)) audioCodecs.Add(jellyfinName);
        }

        await AddAudioAsync("aac", CodecSubtypes.AudioFormatAac);
        await AddAudioAsync("mp3", CodecSubtypes.AudioFormatMP3);
        await AddAudioAsync("ac3", CodecSubtypes.AudioFormatDolbyAC3);
        await AddAudioAsync("eac3", CodecSubtypes.AudioFormatDolbyDDPlus);
        await AddAudioAsync("dts", CodecSubtypes.AudioFormatDts);
        await AddAudioAsync("flac", CodecSubtypes.AudioFormatFlac);
        await AddAudioAsync("alac", CodecSubtypes.AudioFormatAlac);

        var videoCodecs = new List<string>();
        async Task AddVideoAsync(string jellyfinName, string subtype)
        {
            if (await HasDecoderAsync(CodecKind.Video, subtype)) videoCodecs.Add(jellyfinName);
        }

        await AddVideoAsync("h264", CodecSubtypes.VideoFormatH264);
        await AddVideoAsync("hevc", CodecSubtypes.VideoFormatHevc);
        await AddVideoAsync("vp9", CodecSubtypes.VideoFormatVP90);
        await AddVideoAsync("vp8", CodecSubtypes.VideoFormatVP80);
        await AddVideoAsync("mpeg2video", CodecSubtypes.VideoFormatMpeg2);

        // Prefer AAC as the known-good audio transcode target, then MP3.
        var transcodeAudio = audioCodecs.Contains("aac") ? "aac"
            : audioCodecs.Contains("mp3") ? "mp3"
            : "aac";
        var transcodeVideo = videoCodecs.Contains("h264") ? "h264" : "h264";

        var audio = string.Join(',', audioCodecs);
        var video = string.Join(',', videoCodecs);

        var directPlayProfiles = new List<object>();
        if (!string.IsNullOrWhiteSpace(video) && !string.IsNullOrWhiteSpace(audio))
        {
            // Keep Direct Play conservative for containers with unreliable timestamps.
            directPlayProfiles.Add(new
            {
                Container = "mp4,m4v,mov",
                Type = "Video",
                VideoCodec = video,
                AudioCodec = audio
            });
        }

        if (!string.IsNullOrWhiteSpace(audio))
        {
            directPlayProfiles.Add(new
            {
                Container = "mp3,m4a,aac,flac,wav,wma",
                Type = "Audio",
                AudioCodec = audio
            });
        }

        return new
        {
            Name = "Lumen Windows Native (runtime detected)",
            MaxStreamingBitrate = 120_000_000,
            MaxStaticBitrate = 120_000_000,
            DirectPlayProfiles = directPlayProfiles.ToArray(),
            TranscodingProfiles = new object[]
            {
                new
                {
                    Container = "mp4",
                    Type = "Video",
                    VideoCodec = transcodeVideo,
                    AudioCodec = transcodeAudio,
                    Protocol = "http",
                    Context = "Streaming",
                    MaxAudioChannels = "6",
                    CopyTimestamps = false,
                    EnableSubtitlesInManifest = false
                }
            },
            ContainerProfiles = Array.Empty<object>(),
            CodecProfiles = Array.Empty<object>(),
            SubtitleProfiles = Array.Empty<object>()
        };
    }

    public async Task<PlaybackInfoResponseDto?> GetPlaybackInfoAsync(
        string itemId,
        string? mediaSourceId = null,
        int? audioStreamIndex = null,
        int? subtitleStreamIndex = null,
        long startTimeTicks = 0,
        bool enableDirectPlay = true,
        bool allowAudioStreamCopy = true,
        bool allowVideoStreamCopy = true,
        bool enableDirectStream = true,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();

        var deviceProfile = await BuildNativeDeviceProfileAsync();

        var body = new
        {
            UserId = _userId,
            MediaSourceId = mediaSourceId,
            AudioStreamIndex = audioStreamIndex,
            SubtitleStreamIndex = subtitleStreamIndex ?? -1,
            StartTimeTicks = Math.Max(0, startTimeTicks),
            MaxStreamingBitrate = 120_000_000,
            EnableDirectPlay = enableDirectPlay,
            EnableDirectStream = enableDirectStream,
            EnableTranscoding = true,
            AllowVideoStreamCopy = allowVideoStreamCopy,
            AllowAudioStreamCopy = allowAudioStreamCopy,
            DeviceProfile = deviceProfile
        };

        return await SendAsync<PlaybackInfoResponseDto>(HttpMethod.Post,
            $"/Items/{Esc(itemId)}/PlaybackInfo?userId={Esc(_userId!)}", body, true, ct);
    }

    public async Task RefreshLibraryAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, "/Library/Refresh", null, true, ct);
    }

    public string GetDirectPlayUrl(string itemId, string? mediaSourceId = null)
    {
        EnsureAuthenticated();
        var query = new List<string> { "static=true", $"ApiKey={Esc(_token!)}" };
        if (!string.IsNullOrWhiteSpace(mediaSourceId)) query.Add($"mediaSourceId={Esc(mediaSourceId)}");
        return $"{_baseUrl}/Videos/{Esc(itemId)}/stream?{string.Join("&", query)}";
    }

    public IReadOnlyList<string> GetLyricsAnalysisSourceUrls(string itemId)
    {
        EnsureAuthenticated();

        // Prefer the raw download endpoint for lyric analysis.
        var download =
            $"{_baseUrl}/Items/{Esc(itemId)}/Download?ApiKey={Esc(_token!)}";

        // Fall back to the normal static stream when downloads are unavailable.
        var direct = GetDirectPlayUrl(itemId);

        return [download, direct];
    }

    public string GetNegotiatedUrl(string? negotiatedUrl)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(negotiatedUrl))
            throw new InvalidOperationException("Jellyfin did not provide a compatible playback URL.");

        // Normalise legacy api_key query spelling for Jellyfin 12.
        var modernUrl = Regex.Replace(
            negotiatedUrl,
            @"(?i)([?&])api_key=",
            "$1ApiKey=");

        if (Uri.TryCreate(modernUrl, UriKind.Absolute, out var absolute)) return absolute.ToString();
        return _baseUrl + (modernUrl.StartsWith('/') ? modernUrl : "/" + modernUrl);
    }

    public string GetImageUrl(string itemId, string imageType = "Primary", string? tag = null, int width = 420, int quality = 90)
    {
        EnsureConfigured();
        var query = new List<string> { $"fillWidth={width}", $"quality={quality}" };
        if (!string.IsNullOrWhiteSpace(tag)) query.Add($"tag={Esc(tag)}");
        if (!string.IsNullOrWhiteSpace(_token)) query.Add($"ApiKey={Esc(_token!)}");
        return $"{_baseUrl}/Items/{Esc(itemId)}/Images/{imageType}?{string.Join("&", query)}";
    }

    public string GetImageUrlFit(string itemId, string imageType = "Primary", string? tag = null, int maxWidth = 480, int quality = 90)
    {
        EnsureConfigured();
        var query = new List<string> { $"maxWidth={Math.Max(32, maxWidth)}", $"quality={Math.Clamp(quality, 1, 100)}" };
        if (!string.IsNullOrWhiteSpace(tag)) query.Add($"tag={Esc(tag)}");
        if (!string.IsNullOrWhiteSpace(_token)) query.Add($"ApiKey={Esc(_token!)}");
        return $"{_baseUrl}/Items/{Esc(itemId)}/Images/{imageType}?{string.Join("&", query)}";
    }


    public string GetUserImageUrl(string userId, string? tag = null, int width = 180, int quality = 90)
    {
        EnsureConfigured();
        var query = new List<string> { $"width={Math.Max(32, width)}", $"quality={Math.Clamp(quality, 1, 100)}" };
        if (!string.IsNullOrWhiteSpace(tag)) query.Add($"tag={Esc(tag)}");
        if (!string.IsNullOrWhiteSpace(_token)) query.Add($"ApiKey={Esc(_token!)}");
        return $"{_baseUrl}/Users/{Esc(userId)}/Images/Primary?{string.Join("&", query)}";
    }

    public string GetChapterImageUrl(string itemId, int chapterIndex, string? tag = null, int width = 420, int quality = 88)
    {
        EnsureConfigured();
        var query = new List<string> { $"fillWidth={width}", $"quality={quality}" };
        if (!string.IsNullOrWhiteSpace(tag)) query.Add($"tag={Esc(tag)}");
        if (!string.IsNullOrWhiteSpace(_token)) query.Add($"ApiKey={Esc(_token!)}");
        return $"{_baseUrl}/Items/{Esc(itemId)}/Images/Chapter/{Math.Max(0, chapterIndex)}?{string.Join("&", query)}";
    }



    public Task ReportPlaybackStartAsync(string itemId, long positionTicks, int? audioStreamIndex, int? subtitleStreamIndex, string? mediaSourceId, string? playSessionId, string playMethod, bool isPaused, CancellationToken ct = default)
        => ReportPlaybackAsync("/Sessions/Playing", new
        {
            ItemId = itemId,
            PositionTicks = Math.Max(0, positionTicks),
            AudioStreamIndex = audioStreamIndex,
            SubtitleStreamIndex = subtitleStreamIndex ?? -1,
            MediaSourceId = mediaSourceId,
            PlaySessionId = playSessionId,
            PlayMethod = playMethod,
            CanSeek = true,
            IsPaused = isPaused,
            IsMuted = false
        }, ct);

    public Task ReportPlaybackProgressAsync(string itemId, long positionTicks, int? audioStreamIndex, int? subtitleStreamIndex, string? mediaSourceId, string? playSessionId, string playMethod, bool isPaused, CancellationToken ct = default)
        => ReportPlaybackAsync("/Sessions/Playing/Progress", new
        {
            ItemId = itemId,
            PositionTicks = Math.Max(0, positionTicks),
            AudioStreamIndex = audioStreamIndex,
            SubtitleStreamIndex = subtitleStreamIndex ?? -1,
            MediaSourceId = mediaSourceId,
            PlaySessionId = playSessionId,
            PlayMethod = playMethod,
            CanSeek = true,
            IsPaused = isPaused,
            IsMuted = false
        }, ct);

    public Task ReportPlaybackStoppedAsync(string itemId, long positionTicks, string? mediaSourceId, string? playSessionId, CancellationToken ct = default)
        => ReportPlaybackAsync("/Sessions/Playing/Stopped", new
        {
            ItemId = itemId,
            PositionTicks = Math.Max(0, positionTicks),
            MediaSourceId = mediaSourceId,
            PlaySessionId = playSessionId,
            Failed = false
        }, ct);

    private async Task ReportPlaybackAsync(string relativePath, object body, CancellationToken ct)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Post, relativePath, body, true, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lumen playback report failed: {ex.Message}");
        }
    }

    public async Task<List<SessionInfoDto>> GetSessionsAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<List<SessionInfoDto>>(HttpMethod.Get, "/Sessions", null, true, ct) ?? [];
    }

    public async Task<List<SyncPlayGroupDto>> GetSyncPlayGroupsAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        return await SendAsync<List<SyncPlayGroupDto>>(HttpMethod.Get, "/SyncPlay/List", null, true, ct) ?? [];
    }

    public async Task CreateSyncPlayGroupAsync(string name, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, $"/SyncPlay/New?groupName={Esc(name)}", null, true, ct);
    }

    public async Task JoinSyncPlayGroupAsync(string groupId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, $"/SyncPlay/Join?groupId={Esc(groupId)}", null, true, ct);
    }

    public async Task LeaveSyncPlayGroupAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, "/SyncPlay/Leave", null, true, ct);
    }

    public async Task SyncPlayPauseAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, "/SyncPlay/Pause", null, true, ct);
    }

    public async Task SyncPlayUnpauseAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, "/SyncPlay/Unpause", null, true, ct);
    }

    public async Task SyncPlaySeekAsync(long positionTicks, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await SendAsync<object>(HttpMethod.Post, "/SyncPlay/Seek",
            new { PositionTicks = Math.Max(0, positionTicks) }, true, ct);
    }

    public sealed record DownloadTransferProgress(
        double Percent,
        double BytesPerSecond,
        TimeSpan? Eta,
        long BytesTransferred,
        long? TotalBytes);

    public async Task DownloadItemAsync(
        string itemId,
        string destinationPath,
        bool transcode = false,
        int? maxHeight = null,
        int? videoBitrate = null,
        int audioBitrate = 192_000,
        IProgress<DownloadTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        EnsureConfigured();

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var existingLength = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0L;

        var relativeUrl = $"/Items/{Esc(itemId)}/Download";
        if (transcode)
        {
            var query = new List<string>
            {
                "static=false",
                "container=mp4",
                "videoCodec=h264",
                "audioCodec=aac",
                $"audioBitRate={Math.Max(64_000, audioBitrate)}",
                "allowVideoStreamCopy=false",
                "allowAudioStreamCopy=true",
                "context=Streaming"
            };
            if (maxHeight is > 0) query.Add($"maxHeight={maxHeight.Value}");
            if (videoBitrate is > 0) query.Add($"videoBitRate={videoBitrate.Value}");
            relativeUrl = $"/Videos/{Esc(itemId)}/stream.mp4?{string.Join("&", query)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + relativeUrl);
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(includeToken: true));
        request.Headers.TryAddWithoutValidation("Accept", "video/*,audio/*,application/octet-stream");

        if (existingLength > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            throw new JellyfinApiException(response.StatusCode, text);
        }

        var isPartialResponse = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (existingLength > 0 && !isPartialResponse)
        {
            // Restart if the server ignored the Range request.
            existingLength = 0;
        }

        var responseLength = response.Content.Headers.ContentLength;
        long? total = response.Content.Headers.ContentRange?.Length
            ?? (responseLength is > 0 ? responseLength.Value + existingLength : null);

        var buffer = new byte[1024 * 256];
        long writtenThisRun = 0;
        var lastReportUtc = DateTime.UtcNow;
        var sampleStartedUtc = DateTime.UtcNow;
        long sampleStartedBytes = existingLength;
        double smoothedSpeed = 0;

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        {
            await using (var output = new FileStream(
                destinationPath,
                existingLength > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                1024 * 256,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    writtenThisRun += read;

                    var now = DateTime.UtcNow;
                    if (now - lastReportUtc >= TimeSpan.FromMilliseconds(500))
                    {
                        var absoluteWritten = existingLength + writtenThisRun;
                        var sampleSeconds = Math.Max(0.001, (now - sampleStartedUtc).TotalSeconds);
                        var instantSpeed = Math.Max(0, absoluteWritten - sampleStartedBytes) / sampleSeconds;
                        smoothedSpeed = smoothedSpeed <= 0 ? instantSpeed : (smoothedSpeed * 0.70) + (instantSpeed * 0.30);

                        var percent = total is > 0
                            ? Math.Clamp(absoluteWritten * 100d / total.Value, 0, 100)
                            : 0;
                        TimeSpan? eta = total is > 0 && smoothedSpeed > 1
                            ? TimeSpan.FromSeconds(Math.Max(0, total.Value - absoluteWritten) / smoothedSpeed)
                            : null;

                        progress?.Report(new DownloadTransferProgress(percent, smoothedSpeed, eta, absoluteWritten, total));
                        lastReportUtc = now;
                        sampleStartedUtc = now;
                        sampleStartedBytes = absoluteWritten;
                    }
                }

                await output.FlushAsync(ct);
            }
        }

        var completedBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : existingLength + writtenThisRun;
        progress?.Report(new DownloadTransferProgress(100, smoothedSpeed, TimeSpan.Zero, completedBytes, total));
    }

    public async Task<string?> GetThemeVideoUrlAsync(string itemId, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        var result = await SendAsync<QueryResult<BaseItemDto>>(
            HttpMethod.Get,
            $"/Items/{Esc(itemId)}/ThemeVideos?userId={Esc(_userId!)}&inheritFromParent=true",
            null, true, ct);
        var theme = result?.Items.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Id));
        return theme?.Id is { Length: > 0 } id ? GetDirectPlayUrl(id) : null;
    }

    public void SignOut()
    {
        _token = null;
        _userId = null;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? body, bool authenticated, CancellationToken ct)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(method, _baseUrl + relativePath);

        // Jellyfin 12 uses the MediaBrowser Authorization scheme.
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            BuildAuthorizationHeader(includeToken: authenticated));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new JellyfinApiException(response.StatusCode, content);
        if (string.IsNullOrWhiteSpace(content)) return default;
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private string BuildAuthorizationHeader(bool includeToken = false)
    {
        var header = $"MediaBrowser Client=\"{ClientName}\", Device=\"Windows\", DeviceId=\"{_deviceId}\", Version=\"{AppInfo.InformationalVersion}\"";
        if (includeToken && !string.IsNullOrWhiteSpace(_token))
            header += $", Token=\"{_token}\"";
        return header;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("Configure a Jellyfin server first.");
    }

    private void EnsureAuthenticated()
    {
        EnsureConfigured();
        if (!IsAuthenticated)
            throw new InvalidOperationException("Authenticate with Jellyfin first.");
    }

    private static string Esc(string value) => Uri.EscapeDataString(value);

    private static string NormalizeServerUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new UriFormatException("Enter a valid HTTP or HTTPS Jellyfin server URL.");
        return uri.ToString().TrimEnd('/');
    }

    private static string GetDeviceId()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen");
        var path = Path.Combine(folder, "device-id.txt");
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }
        catch { return Guid.NewGuid().ToString("N"); }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class JellyfinApiException(HttpStatusCode statusCode, string response)
    : Exception($"Jellyfin API returned {(int)statusCode} ({statusCode}).")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Response { get; } = response;
}
