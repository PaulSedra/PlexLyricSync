namespace PlexLyricSync.Models;

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