using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PlexLyricSync.Models;

namespace PlexLyricSync.Clients;

public sealed class PlexApiClient(string plexBaseUrl, string plexToken)
{
    private static readonly HttpClient Http = Clients.Http.Client;
    private readonly string _baseUrl = plexBaseUrl.TrimEnd('/');
    private readonly IReadOnlyDictionary<string, string> _headers = new Dictionary<string, string>
    {
        ["X-Plex-Token"] = plexToken,
        ["Cache-Control"] = "no-cache"
    };
    private Dictionary<string, string> _headersWithClientId(string clientId) => new(_headers)
    {
        ["X-Plex-Client-Identifier"] = clientId
    };

    public async Task<PlexampSession?> GetPlexampSession(CancellationToken ct)
    {
        using HttpRequestMessage request = Clients.Http.HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/status/sessions", _headers);

        HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        string xml = await response.Content.ReadAsStringAsync(ct);

        XDocument doc = XDocument.Parse(xml);

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

        XElement tr = plexamp.Track;
        XElement? player = tr.Element("Player");

        string ratingKey = tr.Attribute("ratingKey")?.Value ?? "";
        string artist = tr.Attribute("grandparentTitle")?.Value ?? "";
        string album = tr.Attribute("parentTitle")?.Value ?? "";
        string title = tr.Attribute("title")?.Value ?? "";
        int duration = int.TryParse(tr.Attribute("duration")?.Value, out int d) ? d : 0;

        // Prefer <TranscodeSession time="..."> when present; fallback to viewOffset
        XElement? tcs = tr.Element("TranscodeSession");
        if (!(tcs != null && int.TryParse(tcs.Attribute("time")?.Value, out int offset)))
            offset = int.TryParse(tr.Attribute("viewOffset")?.Value, out int o) ? o : 0;

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

        return new PlexampSession(ratingKey, artist, album, title, offset, duration, state, clientUrl, clientId, artUrl);
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

        using HttpRequestMessage request = Clients.Http.HttpRequestMessage(HttpMethod.Get, $"{clientUrl}/player/playback/seekTo?offset={offsetMs}", _headersWithClientId(clientId));
        HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Sends a pause command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PauseAsync(string clientUrl, string clientId, CancellationToken ct) =>
        SendPlaybackCommandAsync(clientUrl, clientId, "pause", ct);

    /// <summary>
    /// Sends a play command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> PlayAsync(string clientUrl, string clientId, CancellationToken ct) =>
        SendPlaybackCommandAsync(clientUrl, clientId, "play", ct);

    /// <summary>
    /// Sends a skipNext command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipNextAsync(string clientUrl, string clientId, CancellationToken ct) =>
        SendPlaybackCommandAsync(clientUrl, clientId, "skipNext", ct);

    /// <summary>
    /// Send a skipPrevious command to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="ct">cancellation token</param>
    public Task<bool> SkipPreviousAsync(string clientUrl, string clientId, CancellationToken ct) =>
        SendPlaybackCommandAsync(clientUrl, clientId, "skipPrevious", ct);

    /// <summary>
    /// Sends a playback command (play/pause/skipNext/skipPrevious) to the specified Plex client.
    /// </summary>
    /// <param name="clientUrl">id of player client to control</param>
    /// <param name="clientId">id of player client to control</param>
    /// <param name="command"></param>
    /// <param name="ct">cancellation token</param>
    private async Task<bool> SendPlaybackCommandAsync(string clientUrl, string clientId, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        using HttpRequestMessage request = Clients.Http.HttpRequestMessage(HttpMethod.Get, $"{clientUrl}/player/playback/{command}", _headersWithClientId(clientId));
        HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.IsSuccessStatusCode;
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

        using HttpRequestMessage request = Clients.Http.HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/library/metadata/{ratingKey}", _headers);
        HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return false;

        string xml = await response.Content.ReadAsStringAsync(ct);
        return xml.Contains("userRating=\"10.0\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sets whether a Plex music track is "liked" (hearted) by setting its userRating.
    /// "liked" -> rating=10, "unrated" -> rating=0.
    /// </summary>
    /// <param name="ratingKey">The track's Plex ratingKey (metadata id)</param>
    /// <param name="liked">true to like/heart, false to clear/unrate</param>
    /// <param name="ct">Cancellation token</param>
    public async Task SetTrackLikedAsync(string ratingKey, bool liked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ratingKey)) return;

        int rating = liked ? 10 : -1;
        string requestUrl = $"{_baseUrl}/:/rate" + $"?key={Uri.EscapeDataString(ratingKey)}" + $"&identifier=com.plexapp.plugins.library" + $"&rating={rating}";

        using HttpRequestMessage request = Clients.Http.HttpRequestMessage(HttpMethod.Put, requestUrl, _headers);
        await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}
