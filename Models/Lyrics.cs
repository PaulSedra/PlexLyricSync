using System;

namespace PlexLyricSync.Models;

public sealed record Lyrics(string? Synced, string? Plain, string? Path);
public sealed record SyncedLyricLine(TimeSpan T, string Text);


// JSON shape from LRCLIBs
internal sealed record LrcLibResponse
{
    public string? SyncedLyrics { get; set; }
    public string? PlainLyrics { get; set; }
}

// JSON shape from LibreTranslate
internal sealed record LibreTranslateResponse
{
    public string? TranslatedText { get; init; }
}
