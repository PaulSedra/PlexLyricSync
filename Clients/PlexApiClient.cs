using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlexLyricSync.Clients;

public record PlexNowPlayingResult(
    string RatingKey,
    string Artist,
    string Album,
    string Title,
    int ViewOffsetMs,
    int DurationMs,
    string State,
    string ClientUrl,
    string ClientId,
    string AlbumArtUrl
);

public sealed class PlexApiClient(string plexBaseUrl, string plexToken)
{
    private static readonly HttpClient _http = Http.Client;
    private readonly string _baseUrl = plexBaseUrl.TrimEnd('/');

    public async Task<PlexNowPlayingResult?> GetPlexampNowPlayingAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/status/sessions");
        req.Headers.TryAddWithoutValidation("X-Plex-Token", plexToken);
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

        string ratingKey = tr.Attribute("ratingKey")?.Value ?? "";
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
        string clientUrl = player?.Attribute("address")?.Value ?? "";
        string clientId = player?.Attribute("machineIdentifier")?.Value ?? "";

        // album art
        string artPath = tr.Attribute("thumb")?.Value
            ?? tr.Attribute("art")?.Value
            ?? string.Empty;
        string artUrl = string.IsNullOrWhiteSpace(artPath)
            ? string.Empty
            : $"{_baseUrl}{artPath}?X-Plex-Token={plexToken}";

        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            return null;

        return new PlexNowPlayingResult(ratingKey, artist, album, title, offset, duration, state, clientUrl, clientId, artUrl);
    }

    /// <summary>
    /// Asynchronously seeks plexamp player to specified offset.
    /// </summary>
    /// <param name="clientUrl">url of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="offsetMs">position to seek to</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public async Task<bool> SeekToAsync(string clientUrl, string clientId, int offsetMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;
        if (offsetMs < 0) offsetMs = 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{clientUrl}/player/playback/seekTo?offset={offsetMs}");
        req.Headers.TryAddWithoutValidation("X-Plex-Token", plexToken);
        req.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientId);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// Sends a pause command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PauseAsync(string clientUrl, string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientUrl, clientId, "pause", ct);

    /// <summary>
    /// Sends a play command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PlayAsync(string clientUrl, string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientUrl, clientId, "play", ct);

    /// <summary>
    /// Sends a skipNext command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipNextAsync(string clientUrl, string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientUrl, clientId, "skipNext", ct);

    /// <summary>
    /// Send a skipPrevious command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipPreviousAsync(string clientUrl, string clientId, CancellationToken ct) => SendPlaybackCommandAsync(clientUrl, clientId, "skipPrevious", ct);

    /// <summary>
    /// Sends a playback command (play/pause/skipNext/skipPrevious) to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="command"></param>
    /// <param name="ct">cancellation token</param>
    public async Task<bool> SendPlaybackCommandAsync(string clientUrl, string clientId, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{clientUrl}/player/playback/{command}");
        req.Headers.TryAddWithoutValidation("X-Plex-Token", plexToken);
        req.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientId);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>
    /// Gets whether a Plex music track is "liked" (hearted) by checking its userRating.
    /// Plex uses userRating 1-10; most clients use 10 as "liked" and 0/empty as "unrated".
    /// </summary>
    /// <param name="ratingKey">The track's Plex ratingKey (metadata id)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>true if liked; false if unrated or unknown</returns>
    public async Task<bool> GetTrackLikedAsync(string ratingKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ratingKey)) return false;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/library/metadata/{ratingKey}");
        req.Headers.TryAddWithoutValidation("X-Plex-Token", plexToken);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        req.Headers.TryAddWithoutValidation("Accept", "application/xml");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) return false;

        var xml = await resp.Content.ReadAsStringAsync(ct);
        return xml.Contains("userRating=\"10.0\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets whether a Plex music track is "liked" (hearted) by setting its userRating.
    /// "liked" -> rating=10, "unrated" -> rating=0.
    /// </summary>
    /// <param name="ratingKey">The track's Plex ratingKey (metadata id)</param>
    /// <param name="liked">true to like/heart, false to clear/unrate</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<bool> SetTrackLikedAsync(string ratingKey, bool liked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ratingKey)) return false;

        var rating = liked ? 10 : -1;
        var url = $"{_baseUrl}/:/rate" + $"?key={Uri.EscapeDataString(ratingKey)}" + $"&identifier=com.plexapp.plugins.library" + $"&rating={rating}";

        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.TryAddWithoutValidation("X-Plex-Token", plexToken);
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return resp.IsSuccessStatusCode;
    }
}
