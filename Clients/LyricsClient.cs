using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using PlexLyricSync.Models;
using PlexLyricSync.Utils;

namespace PlexLyricSync.Clients;

public static class LyricsClient
{
    private static readonly HttpClient Http = Clients.Http.Client;
    private const string LrclibEndpoint = "https://lrclib.net/api/get";

    /// <summary>
    /// Grabs local lyrics file.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): local lyrics if found</returns>
    public static async Task<Lyrics?> GetLocalLyricsAsync(string artist, string album, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return null;
        return await LrcParser.ReadLyricsAsync(artist, album, title, ct);
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
    public static async Task<Lyrics?> GetRemoteLyricsAsync(string artist, string album, string title, int duration, CancellationToken ct)
    {
        // grab lyrics from lrclib.net by matching aatd
        string url = $"{LrclibEndpoint}?artist_name={Uri.EscapeDataString(artist)}&album_name={Uri.EscapeDataString(album)}&track_name={Uri.EscapeDataString(title)}&duration={Uri.EscapeDataString(duration.ToString())}";
        HttpResponseMessage resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        LrcLibResponse? doc = await resp.Content.ReadFromJsonAsync<LrcLibResponse>(cancellationToken: ct);
        Lyrics lyrics = new(doc?.SyncedLyrics, doc?.PlainLyrics, null);
        if (doc is not null) return LrcParser.WriteLyrics(lyrics, artist, album, title);

        // grab lyrics from lrclib.net by matthing atd
        url = $"{LrclibEndpoint}?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}&duration={Uri.EscapeDataString(duration.ToString())}";
        resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        doc = await resp.Content.ReadFromJsonAsync<LrcLibResponse>(cancellationToken: ct);
        lyrics = new Lyrics(doc?.SyncedLyrics, doc?.PlainLyrics, null);
        return doc is null ? null : LrcParser.WriteLyrics(lyrics, artist, album, title);
    }
}
