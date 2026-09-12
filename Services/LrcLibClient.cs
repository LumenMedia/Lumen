using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lumen.Models;

namespace Lumen.Services;

public sealed class LrcLibClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    public LrcLibClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Lumen/{AppInfo.InformationalVersion} (Jellyfin desktop client)");
    }

    public async Task<List<LrcLibResultDto>> SearchAsync(
        string track, string artist, string? album = null, double? durationSeconds = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track))
            return [];

        var collected = new Dictionary<long, LrcLibResultDto>();

        async Task AddSearchAsync(string query)
        {
            using var response = await _http.GetAsync(query, ct);
            response.EnsureSuccessStatusCode();
            var results = await response.Content.ReadFromJsonAsync<List<LrcLibResultDto>>(cancellationToken: ct) ?? [];
            foreach (var item in results)
                collected.TryAdd(item.Id, item);
            await Task.Delay(220, ct);
        }

        // Exact artist first.
        if (!string.IsNullOrWhiteSpace(artist))
        {
            var exact = $"api/search?track_name={Uri.EscapeDataString(track)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrWhiteSpace(album))
                exact += $"&album_name={Uri.EscapeDataString(album)}";
            await AddSearchAsync(exact);
        }

        // Title-only catches covers catalogued under the original artist.
        if (collected.Count < 4)
            await AddSearchAsync($"api/search?track_name={Uri.EscapeDataString(track)}");

        // Retry without common edition/live/remaster decorations.
        var cleanTitle = CleanTitle(track);
        if (!string.Equals(cleanTitle, track, StringComparison.OrdinalIgnoreCase) && collected.Count < 8)
            await AddSearchAsync($"api/search?track_name={Uri.EscapeDataString(cleanTitle)}");

        // Finish with a broad title search.
        if (collected.Count < 8)
            await AddSearchAsync($"api/search?q={Uri.EscapeDataString(cleanTitle)}");

        var normalizedTarget = Normalize(cleanTitle);
        return collected.Values
            .Where(x => !x.Instrumental &&
                        (!string.IsNullOrWhiteSpace(x.SyncedLyrics) || !string.IsNullOrWhiteSpace(x.PlainLyrics)))
            .OrderByDescending(x => string.Equals(Normalize(CleanTitle(x.TrackName ?? "")), normalizedTarget, StringComparison.Ordinal))
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics))
            .ThenBy(x => durationSeconds is > 0 && x.Duration is > 0
                ? Math.Abs(x.Duration.Value - durationSeconds.Value) : 9999)
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(artist) &&
                                   string.Equals(Normalize(x.ArtistName ?? ""), Normalize(artist), StringComparison.Ordinal))
            .Take(20)
            .ToList();
    }


    public async Task<List<LrcLibResultDto>> ManualSearchAsync(
        string query, double? durationSeconds = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        using var response = await _http.GetAsync($"api/search?q={Uri.EscapeDataString(query.Trim())}", ct);
        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<LrcLibResultDto>>(cancellationToken: ct) ?? [];

        return results
            .Where(x => !x.Instrumental &&
                        (!string.IsNullOrWhiteSpace(x.SyncedLyrics) || !string.IsNullOrWhiteSpace(x.PlainLyrics)))
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics))
            .ThenBy(x => durationSeconds is > 0 && x.Duration is > 0
                ? Math.Abs(x.Duration.Value - durationSeconds.Value) : 9999)
            .Take(30)
            .ToList();
    }

    private static string CleanTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = Regex.Replace(value, @"\s*[\(\[](?:cover|live|acoustic|remaster(?:ed)?|radio edit|edit|version|demo|karaoke|instrumental|feat\.?[^)\]]*)[\)\]]\s*", " ",
            RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*[-–—]\s*(?:cover|live|acoustic|remaster(?:ed)?|radio edit|edit|version|demo|karaoke|instrumental).*$", "",
            RegexOptions.IgnoreCase);
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }

    private static string Normalize(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
}
