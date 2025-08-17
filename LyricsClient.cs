using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync;

public sealed class LyricsClient : IDisposable
{
    public record LyricsData(string? syncedLrc, string? plain, string? path);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// Checks if string is a valid folder/file name and replaces invalid characters with "_".
    /// </summary>
    /// <param name="s">string to check</param>
    /// <returns>A valid folder/file name</returns>
    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Unknown";
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    /// <summary>
    /// Creates lyrics directory if missing and returns the path.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <returns>Lyrics directory path</returns>
    private static string GetLyricsDirectory(string artist, string album)
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var directory = Path.Combine(music, "PlexLyricSync", "lyrics", Sanitize(artist), Sanitize(album));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Grabs local lyrics file.
    /// </summary>
    /// <param name="title">track title</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): lyrics if found</returns>
    public async Task<LyricsData?> GetLocalAsync(string title, string artist, string album, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;

        // file path
        var directory = GetLyricsDirectory(artist, album);
        var lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(directory, Sanitize(title) + ".txt");

        // check if file exists
        if (File.Exists(lrcPath))
        {
            var lrc = await File.ReadAllTextAsync(lrcPath, ct);
            return new LyricsData(lrc, null, lrcPath);
        }
        if (File.Exists(txtPath))
        {
            var plain = await File.ReadAllTextAsync(txtPath, ct);
            return new LyricsData(null, plain, txtPath);
        }

        return null;
    }

    /// <summary>
    /// Grabs lyrics from lrclib.net.
    /// </summary>
    /// <param name="title">track title</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): lyrics if found</returns>
    public async Task<LyricsData?> GetRemoteAsync(string title, string artist, string album, CancellationToken ct)
    {
        // file path
        var directory = GetLyricsDirectory(artist, album);
        var lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(directory, Sanitize(title) + ".txt");

        // grab lyrics from lrclib.net
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var doc = await resp.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
        if (doc is null) return null;

        // write file locally
        if (!string.IsNullOrWhiteSpace(doc.syncedLyrics))
        {
            File.WriteAllText(lrcPath, doc.syncedLyrics);
            return new LyricsData(doc.syncedLyrics, null, lrcPath);
        }
        else if (!string.IsNullOrWhiteSpace(doc.plainLyrics))
        {
            File.WriteAllText(txtPath, doc.plainLyrics);
            return new LyricsData(null, doc.plainLyrics, txtPath);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}

// JSON shape from LRCLIBs
sealed class LrcDoc
{
    public string? syncedLyrics { get; set; }
    public string? plainLyrics { get; set; }
}