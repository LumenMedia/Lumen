using System.Text.Json.Serialization;

namespace Lumen.Models;

public sealed class PublicSystemInfo
{
    public string? ServerName { get; set; }
    public string? Version { get; set; }
    public string? Id { get; set; }
    public string? LocalAddress { get; set; }
}

public sealed class AuthenticateResponse
{
    public UserDto? User { get; set; }
    public SessionInfo? SessionInfo { get; set; }
    public string? AccessToken { get; set; }
    public string? ServerId { get; set; }
}

public sealed class UserDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ServerId { get; set; }
    public string? PrimaryImageTag { get; set; }
    public UserPolicy? Policy { get; set; }
}

public sealed class UserPolicy
{
    public bool IsAdministrator { get; set; }
}

public sealed class SessionInfo
{
    public string? Id { get; set; }
    public string? UserId { get; set; }
    public string? Client { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class QueryResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalRecordCount { get; set; }
}

public sealed class BaseItemDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? CollectionType { get; set; }
    public string? ServerId { get; set; }
    public Dictionary<string, string>? ImageTags { get; set; }
    public List<string>? BackdropImageTags { get; set; }
    public string? PrimaryImageTag { get; set; }
    public int? ProductionYear { get; set; }
    public DateTime? PremiereDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public string? Overview { get; set; }
    public double? CommunityRating { get; set; }
    public double? CriticRating { get; set; }
    public string? OfficialRating { get; set; }
    public List<string>? Taglines { get; set; }
    public long? RunTimeTicks { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public string? ParentId { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public string? Album { get; set; }
    public string? AlbumId { get; set; }
    public string? AlbumArtist { get; set; }
    public List<string>? Artists { get; set; }
    public List<NameIdPairDto>? ArtistItems { get; set; }
    public List<NameIdPairDto>? AlbumArtists { get; set; }
    public bool IsFolder { get; set; }
    public UserItemDataDto? UserData { get; set; }
    public List<string>? Genres { get; set; }
    public List<MediaStreamDto>? MediaStreams { get; set; }
    public List<PersonDto>? People { get; set; }
    public List<NameIdPairDto>? Studios { get; set; }
    public List<string>? ProductionLocations { get; set; }
    public List<ChapterInfoDto>? Chapters { get; set; }
    public int? ChildCount { get; set; }
    public List<MediaSourceInfoDto>? MediaSources { get; set; }
}

public sealed class NameIdPairDto
{
    public string? Name { get; set; }
    public string? Id { get; set; }
}

public sealed class UserItemDataDto
{
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public long PlaybackPositionTicks { get; set; }
    public long PlayCount { get; set; }
}

public sealed class MediaStreamDto
{
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? DisplayTitle { get; set; }
    public string? Type { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public int Index { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? BitRate { get; set; }
    public int? Channels { get; set; }
    public string? VideoRange { get; set; }
    public string? VideoRangeType { get; set; }
    public double? RealFrameRate { get; set; }
}

public sealed class PersonDto
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Role { get; set; }
    public string? Type { get; set; }
    public string? PrimaryImageTag { get; set; }
}

public sealed class PlaybackInfoResponseDto
{
    public List<MediaSourceInfoDto> MediaSources { get; set; } = [];
    public string? PlaySessionId { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class PlaybackSelection
{
    public string ItemId { get; set; } = string.Empty;
    public string? MediaSourceId { get; set; }
    public int? AudioStreamIndex { get; set; }
    public int? SubtitleStreamIndex { get; set; }
    public long ResumePositionTicks { get; set; }
    public bool StartFromBeginning { get; set; }
    public string PlayMethod { get; set; } = "DirectStream";
    public string? PlaySessionId { get; set; }
    public string? LocalFilePath { get; set; }
    public bool IsOffline { get; set; }
}

public sealed class MediaSourceInfoDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Container { get; set; }
    public string? Path { get; set; }
    public string? Protocol { get; set; }
    public int? Bitrate { get; set; }
    public long? Size { get; set; }
    public bool SupportsDirectPlay { get; set; }
    public bool SupportsDirectStream { get; set; }
    public bool SupportsTranscoding { get; set; }
    public string? VideoType { get; set; }
    public string? TranscodingUrl { get; set; }
    public string? TranscodingContainer { get; set; }
    public string? TranscodingSubProtocol { get; set; }
    public int? DefaultAudioStreamIndex { get; set; }
    public int? DefaultSubtitleStreamIndex { get; set; }
    public string? DirectStreamUrl { get; set; }
    public List<MediaStreamDto>? MediaStreams { get; set; }
    public List<string>? TranscodingReasons { get; set; }
}


public sealed class MediaSegmentDto
{
    public string? Id { get; set; }
    public string? ItemId { get; set; }
    public string? Type { get; set; }
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
}

public sealed class ChapterInfoDto
{
    public long StartPositionTicks { get; set; }
    public string? Name { get; set; }
    public string? ImagePath { get; set; }
    public string? ImageTag { get; set; }
}


public sealed class SessionInfoDto
{
    public string? Id { get; set; }
    public string? DeviceName { get; set; }
    public string? Client { get; set; }
    public string? UserName { get; set; }
    public BaseItemDto? NowPlayingItem { get; set; }
    public PlaybackStateInfoDto? PlayState { get; set; }
    public TranscodingInfoDto? TranscodingInfo { get; set; }
}

public sealed class PlaybackStateInfoDto
{
    public string? PlayMethod { get; set; }
    public bool IsPaused { get; set; }
    public long? PositionTicks { get; set; }
}

public sealed class TranscodingInfoDto
{
    public string? Container { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? AudioBitrate { get; set; }
    public int? VideoBitrate { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Framerate { get; set; }
    public double? TranscodingFramerate { get; set; }
    public double? CompletionPercentage { get; set; }
    public double? Speed { get; set; }
    public bool? IsVideoDirect { get; set; }
    public bool? IsAudioDirect { get; set; }
    public bool? IsVideoHWTranscoding { get; set; }
    public bool? IsThrottled { get; set; }
    public string? HardwareAccelerationType { get; set; }
}

public sealed class SyncPlayGroupDto
{
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? State { get; set; }
    public List<string>? Participants { get; set; }
}

public sealed class LyricLineDto
{
    public long? Start { get; set; }
    public string? Text { get; set; }
}
public sealed class LyricsResultDto
{
    public List<LyricLineDto> Lyrics { get; set; } = [];
}

public sealed class RemoteLyricInfoDto
{
    public string? Id { get; set; }
    public string? ProviderName { get; set; }
    public LyricsResultDto? Lyrics { get; set; }
}

public sealed class LrcLibResultDto
{
    public long Id { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public double? Duration { get; set; }
    public bool Instrumental { get; set; }
    public string? PlainLyrics { get; set; }
    public string? SyncedLyrics { get; set; }
}
