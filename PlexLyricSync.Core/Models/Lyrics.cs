using System;

namespace PlexLyricSync.Core.Models;

public sealed record Lyrics(string? Synced, string? Plain, string? Path);
public sealed record SyncedLyricLine(TimeSpan T, string Text);


// JSON shape from LRCLIBs
public sealed record LrcLibResponse
{
    public string? SyncedLyrics { get; set; }
    public string? PlainLyrics { get; set; }
}

// JSON shape from LibreTranslate
public sealed record LibreTranslateResponse
{
    public string? TranslatedText { get; init; }
}
