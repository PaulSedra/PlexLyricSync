using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Windows.Storage;

namespace PlexLyricSync;

public sealed class LyricsClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// Grabs lrc file locally or from lrclib.net.
    /// </summary>
    /// <param name="title">track title</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>
    ///     SyncedLrc string (optional): synced lyrics if available<br/>
    ///     plain string (optional): plain lyrics if available<br/>
    ///     path string (optional): path to lrc file if local<br/>
    ///     fromCahce bool: whether lrc file is local or remote
    /// </returns>
    public async Task<(string? syncedLrc, string? plain, string? path, bool fromCache)?> GetAsync(string title, string artist, string album, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;

        static string Sanitize(strsing s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Unknown";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        // file path
        var localDir = ApplicationData.Current.LocalFolder.Path;
        var lyricsDir = Path.Combine(localDir, "lyrics", Sanitize(artist), Sanitize(album));
        Directory.CreateDirectory(lyricsDir);

        var lrcPath = Path.Combine(lyricsDir, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(lyricsDir, Sanitize(title) + ".txt");

        // check if file exists
        if (File.Exists(lrcPath))
        {
            var lrc = await File.ReadAllTextAsync(lrcPath, ct);
            return (lrc, null, lrcPath, true);
        }
        if (File.Exists(txtPath))
        {
            var plainCached = await File.ReadAllTextAsync(txtPath, ct);
            return (null, plainCached, txtPath, true);
        }

        // if file not found grab one from lrclib.net
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var doc = await resp.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
        if (doc is null) return null;

        // write file locally
        string? path = null;
        if (!string.IsNullOrWhiteSpace(doc.syncedLyrics))
        {
            File.WriteAllText(lrcPath, doc.syncedLyrics);
            path = lrcPath;
        }
        else if (!string.IsNullOrWhiteSpace(doc.plainLyrics))
        {
            File.WriteAllText(txtPath, doc.plainLyrics);
            path = txtPath;
        }

        return (doc.syncedLyrics, doc.plainLyrics, path, false);
    }

    public void Dispose() => _http.Dispose();
}

// JSON shape from LRCLIB
sealed class LrcDoc
{
    public string? syncedLyrics { get; set; }
    public string? plainLyrics { get; set; }
}