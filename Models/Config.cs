namespace PlexLyricSync.Models;

public sealed record Config
{
    public string PlexBaseUrl { get; set; } = "";
    public string PlexToken { get; set; } = "";
    public int SyncedLyricLines { get; set; } = 3;
    public string LibreTranslateBaseUrl { get; set; } = "";
    public string PreferredLanguage { get; set; } = "";
    public bool UseSystemLanguage { get; set; } = true;
    public bool EnableTranslations { get; set; }
    public bool ShowTranslations { get; set; }
}
