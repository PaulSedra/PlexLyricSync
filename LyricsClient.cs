using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync;

public sealed class LyricsClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Unknown";
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private static string GetLyricsDir(string artist, string album)
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var dir = Path.Combine(music, "PlexLyricSync", "lyrics", Sanitize(artist), Sanitize(album));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<(string? syncedLrc, string? plain, string? path)?> GetLocalAsync(string title, string artist, string album, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;

        var dir = GetLyricsDir(artist, album);
        var lrcPath = Path.Combine(dir, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(dir, Sanitize(title) + ".txt");

        if (File.Exists(lrcPath))
        {
            var lrc = await File.ReadAllTextAsync(lrcPath, ct);
            return (lrc, null, lrcPath);
        }
        if (File.Exists(txtPath))
        {
            var plain = await File.ReadAllTextAsync(txtPath, ct);
            return (null, plain, txtPath);
        }

        return null;
    }

    public async Task<(string? syncedLrc, string? plain, string? path)> GetRemoteAsync(string title, string artist, string album, CancellationToken ct)
    {
        var dir = GetLyricsDir(artist, album);
        var lrcPath = Path.Combine(dir, Sanitize(title) + ".lrc");
        var txtPath = Path.Combine(dir, Sanitize(title) + ".txt");

        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return (null, null, null);

        var doc = await resp.Content.ReadFromJsonAsync<LrcDoc>(cancellationToken: ct);
        if (doc is null) return (null, null, null);

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

        return (doc.syncedLyrics, doc.plainLyrics, path);
    }

    public void Dispose() => _http.Dispose();
}

sealed class LrcDoc
{
    public string? syncedLyrics { get; set; }
    public string? plainLyrics { get; set; }
}

