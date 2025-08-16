using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlexLyricSync;

public record PlexNowPlayingResult(
    string Artist,
    string Album,
    string Title,
    int ViewOffsetMs,
    int DurationMs,
    string State,
    string ClientId
);

public sealed class PlexApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public PlexApiClient(string plexBaseUrl, string plexToken)
    {
        _baseUrl = plexBaseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Plex-Token", plexToken);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Pragma", "no-cache");
    }

    public async Task<PlexNowPlayingResult?> GetPlexampNowPlayingAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/status/sessions");
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync(ct);

        var doc = XDocument.Parse(xml);

        // Find a Plexamp session (prefer state="playing")
        var plexamp = doc.Descendants("Track")
            .Select(t => new
            {
                Track = t,
                Player = t.Element("Player"),
                IsPlexamp = string.Equals(
                    t.Element("Player")?.Attribute("product")?.Value,
                    "Plexamp",
                    StringComparison.OrdinalIgnoreCase),
                State = t.Element("Player")?.Attribute("state")?.Value ?? ""
            })
            .Where(x => x.IsPlexamp)
            .OrderByDescending(x => string.Equals(x.State, "playing", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (plexamp is null) return null;

        var tr = plexamp.Track;
        var player = tr.Element("Player");

        string artist = tr.Attribute("grandparentTitle")?.Value ?? "";
        string album = tr.Attribute("parentTitle")?.Value ?? "";
        string title = tr.Attribute("title")?.Value ?? "";
        int duration = int.TryParse(tr.Attribute("duration")?.Value, out var d) ? d : 0;

        // Prefer <TranscodeSession time="..."> when present; fallback to viewOffset
        int offset = 0;
        var tcs = tr.Element("TranscodeSession");
        if (!(tcs != null && int.TryParse(tcs.Attribute("time")?.Value, out offset)))
            offset = int.TryParse(tr.Attribute("viewOffset")?.Value, out var o) ? o : 0;

        string state = plexamp.State;
        string clientId = player?.Attribute("machineIdentifier")?.Value ?? "";

        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            return null;

        return new PlexNowPlayingResult(artist, album, title, offset, duration, state, clientId);
    }

    /// <summary>
    /// Asynchronously seeks plexamp player to specified offset.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="offsetMs">position to seek to</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public async Task<bool> SeekToAsync(string clientId, int offsetMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;
        if (offsetMs < 0) offsetMs = 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/player/playback/seekTo?offset={offsetMs}");
        req.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", clientId);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// Sends a pause command to the specified Plex client.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PauseAsync(string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientId, "pause", ct);

    /// <summary>
    /// Sends a play command to the specified Plex client.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PlayAsync(string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientId, "play", ct);

    /// <summary>
    /// Sends a skipNext command to the specified Plex client.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipNextAsync(string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientId, "skipNext", ct);

    /// <summary>
    /// Send a skipPrevious command to the specified Plex client.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipPreviousAsync(string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientId, "skipPrevious", ct);

    /// <summary>
    /// Sends a playback command (play/pause/skipNext/skipPrevious) to the specified Plex client.
    /// </summary>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="command"></param>
    /// <param name="ct">cancellation token</param>
    private async Task<bool> SendPlaybackCommandAsync(string clientId, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/player/playback/{command}");
        req.Headers.TryAddWithoutValidation("X-Plex-Target-Client-Identifier", clientId);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode;
    }

    public void Dispose() => _http.Dispose();
}
