namespace PlexLyricSync.Core.Models;

public sealed record Config
{
    public string PlexBaseUrl { get; set; } = "";
    public string PlexToken { get; set; } = "";
    public int SyncedLyricLines { get; set; } = 3;
    public bool ShowJapaneseTransliteration { get; set; }
}
