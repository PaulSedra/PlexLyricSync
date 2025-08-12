using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync;

public sealed class LyricsClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    // Returns LRC if available; else plain lyrics; else null.
    public async Task<(string? syncedLrc, string? plain)?> GetAsync(string title, string artist, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;

        // Simple query; we can add album/ISRC later
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var doc = await resp.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
        return doc is null ? null : (doc.syncedLyrics, doc.plainLyrics);
    }

    public void Dispose() => _http.Dispose();
}

// JSON shape from LRCLIB
sealed class LrcDoc
{
    public string? syncedLyrics { get; set; }
    public string? plainLyrics { get; set; }
}