using System;

namespace PlexLyricSync.Core.Models;

public sealed record LyricsFile(string? Synced, string? Plain, string? Path);

public sealed record SyncedLyricLine(TimeSpan T, string Text);

// JSON shape from LRCLIB.
public sealed record LrcLibResponse
{
    public string? SyncedLyrics { get; set; }
    public string? PlainLyrics { get; set; }
}
