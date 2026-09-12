using System.Net;
using System.Text;
using Lumen.Models;
using Whisper.net;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Lumen.Services;

public sealed class LyricsAutoSyncResult
{
    public double OffsetSeconds { get; init; }
    public double Scale { get; init; } = 1.0;
    public int MatchCount { get; init; }
    public double Confidence { get; init; }
}

public sealed class LyricsAutoSyncService
{
    private static readonly string ModelPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "AutoSync", "ggml-tiny.bin");
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<LyricsAutoSyncResult> SyncAsync(
        string itemId,
        IReadOnlyList<LyricLineDto> lyrics,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var timedLyrics = lyrics
            .Where(x => x.Start.HasValue && !string.IsNullOrWhiteSpace(x.Text))
            .ToList();

        if (timedLyrics.Count < 3)
            throw new InvalidOperationException("Auto Sync needs at least three timestamped lyric lines.");

        progress?.Report("Preparing Auto Sync…");
        EnsureModelAvailable();

        var workId = Guid.NewGuid().ToString("N");
        string? sourcePath = null;
        var wavPath = Path.Combine(Path.GetTempPath(), $"Lumen-LyricSync-{workId}.wav");

        try
        {
            progress?.Report("Getting song audio…");
            sourcePath = await DownloadAnalysisSourceAsync(itemId, workId, ct);

            progress?.Report("Preparing song audio locally…");
            await ConvertToWhisperWaveAsync(sourcePath, wavPath, ct);
            ValidateWaveFile(wavPath);

            progress?.Report("Listening to the song…");
            var transcript = await TranscribeAsync(wavPath, ct);
            if (transcript.Count < 2)
                throw new InvalidOperationException("Auto Sync could not recognise enough vocals in this track.");

            progress?.Report("Matching vocals to lyrics…");
            var result = CalculateAlignment(timedLyrics, transcript);
            if (result is null)
                throw new InvalidOperationException(
                    "Auto Sync could not confidently match the recognised vocals to these lyrics.");

            return result;
        }
        finally
        {
            try { if (sourcePath is not null && File.Exists(sourcePath)) File.Delete(sourcePath); } catch { }
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }
    }

    private static void EnsureModelAvailable()
    {
        if (!File.Exists(ModelPath))
        {
            throw new FileNotFoundException(
                "Auto Sync model not found. Put ggml-tiny.bin in Assets\\AutoSync and rebuild Lumen.",
                ModelPath);
        }

        if (new FileInfo(ModelPath).Length < 50_000_000)
        {
            throw new InvalidOperationException(
                "Assets\\AutoSync\\ggml-tiny.bin appears to be incomplete or invalid.");
        }
    }

    private async Task<string> DownloadAnalysisSourceAsync(
        string itemId,
        string workId,
        CancellationToken ct)
    {
        var item = await App.Jellyfin.GetItemAsync(itemId);
        var sourceExtension = GetSourceExtension(item);

        int? lastStatus = null;
        string? lastReason = null;

        foreach (var url in App.Jellyfin.GetLyricsAnalysisSourceUrls(itemId))
        {
            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                lastStatus = (int)response.StatusCode;
                lastReason = response.ReasonPhrase;
                continue;
            }

            var extension = sourceExtension;
            if (string.IsNullOrWhiteSpace(extension))
                extension = GetResponseExtension(response);

            if (string.IsNullOrWhiteSpace(extension))
                extension = ".audio";

            var sourcePath = Path.Combine(
                Path.GetTempPath(),
                $"Lumen-LyricSync-{workId}{extension}");

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(
                sourcePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
            }

            if (File.Exists(sourcePath) && new FileInfo(sourcePath).Length > 4096)
                return sourcePath;

            try { File.Delete(sourcePath); } catch { }
        }

        var serverMessage = lastStatus.HasValue
            ? $"Jellyfin returned {lastStatus.Value}" +
              (string.IsNullOrWhiteSpace(lastReason) ? "." : $" ({lastReason}).")
            : "Jellyfin did not return the track.";

        throw new InvalidOperationException(
            $"Could not get the song audio for Auto Sync. {serverMessage}");
    }

    private static string GetSourceExtension(BaseItemDto? item)
    {
        var source = item?.MediaSources?.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(source?.Path))
        {
            var pathExtension = Path.GetExtension(source.Path);
            if (IsSafeMediaExtension(pathExtension))
                return pathExtension.ToLowerInvariant();
        }

        var container = source?.Container?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return ContainerToExtension(container);
    }

    private static string GetResponseExtension(HttpResponseMessage response)
    {
        var fileName =
            response.Content.Headers.ContentDisposition?.FileNameStar ??
            response.Content.Headers.ContentDisposition?.FileName;

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName.Trim('"'));
            if (IsSafeMediaExtension(extension))
                return extension.ToLowerInvariant();
        }

        return response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
        {
            "audio/mpeg" => ".mp3",
            "audio/mp3" => ".mp3",
            "audio/flac" => ".flac",
            "audio/x-flac" => ".flac",
            "audio/mp4" => ".m4a",
            "audio/aac" => ".aac",
            "audio/ogg" => ".ogg",
            "audio/opus" => ".opus",
            "audio/wav" => ".wav",
            "audio/x-wav" => ".wav",
            "audio/webm" => ".webm",
            _ => string.Empty
        };
    }

    private static string ContainerToExtension(string? container)
        => container?.Trim().ToLowerInvariant() switch
        {
            "mp3" => ".mp3",
            "flac" => ".flac",
            "m4a" => ".m4a",
            "mp4" => ".m4a",
            "aac" => ".aac",
            "ogg" => ".ogg",
            "oga" => ".ogg",
            "opus" => ".opus",
            "wav" => ".wav",
            "wave" => ".wav",
            "webm" => ".webm",
            "wma" => ".wma",
            _ => string.Empty
        };

    private static bool IsSafeMediaExtension(string? extension)
        => extension?.ToLowerInvariant() is
            ".mp3" or ".flac" or ".m4a" or ".mp4" or ".aac" or ".ogg" or
            ".oga" or ".opus" or ".wav" or ".wave" or ".webm" or ".wma";

    private static async Task ConvertToWhisperWaveAsync(
        string sourcePath,
        string wavPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Use Windows MediaTranscoder so Auto Sync does not need a bundled ffmpeg.
        var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);

        await File.WriteAllBytesAsync(wavPath, [], ct);
        var outputFile = await StorageFile.GetFileFromPathAsync(wavPath);

        var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);
        profile.Audio = AudioEncodingProperties.CreatePcm(16000, 1, 16);

        var transcoder = new MediaTranscoder();
        var prepared = await transcoder.PrepareFileTranscodeAsync(
            sourceFile,
            outputFile,
            profile);

        if (!prepared.CanTranscode)
        {
            throw new InvalidOperationException(
                $"Windows could not decode this audio format for Auto Sync ({prepared.FailureReason}).");
        }

        ct.ThrowIfCancellationRequested();
        await prepared.TranscodeAsync();
        ct.ThrowIfCancellationRequested();
    }

    private static void ValidateWaveFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 44)
            throw new InvalidOperationException("Jellyfin returned an empty analysis stream.");

        Span<byte> header = stackalloc byte[12];
        if (stream.Read(header) != header.Length ||
            Encoding.ASCII.GetString(header[..4]) != "RIFF" ||
            Encoding.ASCII.GetString(header[8..12]) != "WAVE")
        {
            throw new InvalidOperationException(
                "Jellyfin did not return a PCM WAV stream for lyric analysis.");
        }
    }

    private static async Task<List<TranscriptSegment>> TranscribeAsync(string wavPath, CancellationToken ct)
    {
        var segments = new List<TranscriptSegment>();

        using var factory = WhisperFactory.FromPath(ModelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguageDetection()
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8))
            .SplitOnWord()
            .WithMaxSegmentLength(56)
            .Build();

        await using var file = File.OpenRead(wavPath);
        await foreach (var result in processor.ProcessAsync(file, ct))
        {
            var text = result.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            segments.Add(new TranscriptSegment(
                result.Start.TotalSeconds,
                result.End.TotalSeconds,
                text));
        }

        return segments;
    }

    private static LyricsAutoSyncResult? CalculateAlignment(
        IReadOnlyList<LyricLineDto> lyrics,
        IReadOnlyList<TranscriptSegment> transcript)
    {
        var matches = new List<AlignmentMatch>();

        foreach (var lyric in lyrics)
        {
            if (!lyric.Start.HasValue || string.IsNullOrWhiteSpace(lyric.Text))
                continue;

            var lyricWords = GetWords(lyric.Text);
            if (lyricWords.Count < 2)
                continue;

            var bestScore = 0d;
            var bestCommon = 0;
            TranscriptSegment? bestSegment = null;

            foreach (var segment in transcript)
            {
                var segmentWords = GetWords(segment.Text);
                if (segmentWords.Count == 0)
                    continue;

                var (score, common) = Similarity(lyricWords, segmentWords);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCommon = common;
                    bestSegment = segment;
                }
            }

            if (bestSegment is null)
                continue;

            // Require stronger matches for short lyric lines.
            var requiredScore = lyricWords.Count <= 3 ? .72 : .50;
            var requiredCommon = lyricWords.Count <= 3 ? 2 : Math.Min(3, lyricWords.Count);

            if (bestScore < requiredScore || bestCommon < requiredCommon)
                continue;

            var lyricSeconds = TimeSpan.FromTicks(lyric.Start.Value).TotalSeconds;
            matches.Add(new AlignmentMatch(
                lyricSeconds,
                bestSegment.StartSeconds,
                bestScore));
        }

        if (matches.Count < 3)
            return null;

        // Find a robust global offset before considering drift.
        var rawOffsets = matches.Select(x => x.AudioSeconds - x.LyricSeconds).OrderBy(x => x).ToList();
        var medianOffset = Median(rawOffsets);

        var inliers = matches
            .Where(x => Math.Abs((x.AudioSeconds - x.LyricSeconds) - medianOffset) <= 4.0)
            .OrderBy(x => x.LyricSeconds)
            .ToList();

        if (inliers.Count < 3)
            return null;

        var scale = 1.0;

        // Only allow tiny drift corrections when matches span most of the song.
        var span = inliers[^1].LyricSeconds - inliers[0].LyricSeconds;
        if (inliers.Count >= 5 && span >= 75)
        {
            var meanX = inliers.Average(x => x.LyricSeconds);
            var meanY = inliers.Average(x => x.AudioSeconds);
            var denom = inliers.Sum(x => Math.Pow(x.LyricSeconds - meanX, 2));

            if (denom > 1)
            {
                var fitted = inliers.Sum(x =>
                    (x.LyricSeconds - meanX) * (x.AudioSeconds - meanY)) / denom;

                if (fitted is >= .985 and <= 1.015)
                {
                    var plainResidual = Median(inliers
                        .Select(x => Math.Abs(x.AudioSeconds - (x.LyricSeconds + medianOffset)))
                        .OrderBy(x => x).ToList());

                    var fittedOffset = Median(inliers
                        .Select(x => x.AudioSeconds - fitted * x.LyricSeconds)
                        .OrderBy(x => x).ToList());

                    var fittedResidual = Median(inliers
                        .Select(x => Math.Abs(x.AudioSeconds - (fitted * x.LyricSeconds + fittedOffset)))
                        .OrderBy(x => x).ToList());

                    if (fittedResidual + .20 < plainResidual)
                        scale = fitted;
                }
            }
        }

        var finalOffsets = inliers
            .Select(x => x.AudioSeconds - scale * x.LyricSeconds)
            .OrderBy(x => x)
            .ToList();

        var offset = Median(finalOffsets);
        var confidence = inliers.Average(x => x.Score);

        // Reject offsets over 30 seconds as likely repeated-verse mismatches.
        if (Math.Abs(offset) > 30)
            return null;

        return new LyricsAutoSyncResult
        {
            OffsetSeconds = Math.Round(offset, 2),
            Scale = Math.Round(scale, 6),
            MatchCount = inliers.Count,
            Confidence = confidence
        };
    }

    private static (double Score, int Common) Similarity(
        IReadOnlyList<string> lyricWords,
        IReadOnlyList<string> segmentWords)
    {
        var lyricSet = lyricWords.ToHashSet(StringComparer.Ordinal);
        var segmentSet = segmentWords.ToHashSet(StringComparer.Ordinal);
        var common = lyricSet.Count(segmentSet.Contains);
        if (common == 0) return (0, 0);

        var precision = common / (double)segmentSet.Count;
        var recall = common / (double)lyricSet.Count;
        var f1 = 2 * precision * recall / (precision + recall);

        var lyricPhrase = string.Join(' ', lyricWords);
        var segmentPhrase = string.Join(' ', segmentWords);
        if (segmentPhrase.Contains(lyricPhrase, StringComparison.Ordinal) ||
            lyricPhrase.Contains(segmentPhrase, StringComparison.Ordinal))
            f1 = Math.Min(1, f1 + .14);

        return (f1, common);
    }

    private static List<string> GetWords(string value)
    {
        var normalized = Normalize(value);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(c);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }

    private sealed record TranscriptSegment(double StartSeconds, double EndSeconds, string Text);
    private sealed record AlignmentMatch(double LyricSeconds, double AudioSeconds, double Score);
}
