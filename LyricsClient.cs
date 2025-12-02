using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync;

public sealed class LyricsClient : IDisposable
{
    public record LyricsData(string? syncedLrc, string? plain, string? path);

    private static readonly HttpClient _http = Http.Client;
    private static readonly string LrclibEndpoint = "https://lrclib.net/api/get";

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
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): local lyrics if found</returns>
    public static async Task<LyricsData?> GetLocalLyricsAsync(string artist, string album, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;
        return await ReadLyricsDataAsync(artist, album, title, ct);
    }

    /// <summary>
    /// Grabs lyrics from lrclib.net.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="duration">track duration</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): remote lyrics if found</returns>
    public static async Task<LyricsData?> GetRemoteLyricsAsync(string artist, string album, string title, int duration, CancellationToken ct)
    {
        // grab lyrics from lrclib.net
        var url = $"{LrclibEndpoint}?artist_name={Uri.EscapeDataString(artist)}&album_name={Uri.EscapeDataString(album)}&track_name={Uri.EscapeDataString(title)}&duration={Uri.EscapeDataString(duration.ToString())}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var doc = await resp.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
        if (doc is null)
        {
            var url2 = $"{LrclibEndpoint}?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}&duration={Uri.EscapeDataString(duration.ToString())}";
            var resp2 = await _http.GetAsync(url2, ct);
            var doc2 = await resp2.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
            if (doc2 is null) return null;
            doc = doc2;
        }

        return WriteLyricsDataFromDoc(doc, artist, album, title);
    }

    /// <summary>
    /// Grabs a cached translated lyrics file if it exists.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <returns>Lyrics Data (optional): local translated lyrics if found</returns>
    public static async Task<LyricsData?> GetLocalTranslationAsync(string artist, string album, string title, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage)) return null;
        return await ReadLyricsDataAsync(album, artist, title + "_" + targetLanguage, ct);
    }

    /// <summary>
    /// Translates the provided lyrics using LibreTranslate.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <returns>LyricsData (optional): remote translated lyrics if found</returns>
    public static async Task<LyricsData?> GetRemoteTranslationAsync(LyricsData original, string artist, string album, string title, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage)) return null;
        
        LrcDoc lrcDoc = new();

        // synced lyrics translation
        if (!string.IsNullOrWhiteSpace(original.syncedLrc))
        {
            var parsed = LrcParser.Parse(original.syncedLrc);
            if (parsed.Count == 0) return null;

            var originalLines = parsed.Select(l => l.Text).ToArray();
            var joined = string.Join("\n", originalLines);
            var translated = await LibreTranslateClient.TranslateTextAsync(joined, targetLanguage, ct);

            if (string.IsNullOrWhiteSpace(translated)) lrcDoc.SyncedLyrics = null;
            else lrcDoc.SyncedLyrics = LrcParser.CopyLrcTimeSpans(parsed, translated);
        }
        // plain lyrics translation
        else if (!string.IsNullOrWhiteSpace(original.plain))
        {
            lrcDoc.PlainLyrics = await LibreTranslateClient.TranslateTextAsync(original.plain, targetLanguage, ct);
        }

        return WriteLyricsDataFromDoc(lrcDoc, artist, album, title + "_" + targetLanguage);
    }

    /// <summary>
    /// Read a lyrics file from disk based on track information.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <returns></returns>
    private static async Task<LyricsData?> ReadLyricsDataAsync(string artist, string album, string title, CancellationToken ct)
    {
        // file path
        var directory = GetLyricsDirectory(artist, album);
        var txtPath = Path.Combine(directory, Sanitize(title) + ".txt");
        var lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");

        if (File.Exists(lrcPath))
        {
            var lrc = await File.ReadAllTextAsync(lrcPath, ct);
            return new LyricsData(lrc, null, lrcPath);
        }
        else if (File.Exists(txtPath))
        {
            var plain = await File.ReadAllTextAsync(txtPath, ct);
            return new LyricsData(null, plain, txtPath);
        }

        return null;
    }

    /// <summary>
    /// Writes a lyrics file to disk based on track information.
    /// </summary>
    /// <param name="doc">lyrics doc to be written</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <returns></returns>
    private static LyricsData? WriteLyricsDataFromDoc(LrcDoc doc, string artist, string album, string title)
    {
        // file path
        var directory = GetLyricsDirectory(artist, album);
        var lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(directory, Sanitize(title) + ".txt");

        if (!string.IsNullOrWhiteSpace(doc.SyncedLyrics))
        {
            File.WriteAllText(lrcPath, doc.SyncedLyrics);
            return new LyricsData(doc.SyncedLyrics, null, lrcPath);
        }
        else if (!string.IsNullOrWhiteSpace(doc.PlainLyrics))
        {
            File.WriteAllText(txtPath, doc.PlainLyrics);
            return new LyricsData(null, doc.PlainLyrics, txtPath);
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}

// JSON shape from LRCLIBs
sealed class LrcDoc
{
    public string? SyncedLyrics { get; set; }
    public string? PlainLyrics { get; set; }
}

// JSON shape from LibreTranslate
sealed class LibreTranslateResponse
{
    public string? TranslatedText { get; set; }
}
