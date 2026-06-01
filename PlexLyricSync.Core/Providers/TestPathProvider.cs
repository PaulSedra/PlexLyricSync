using PlexLyricSync.Core.Providers;

public sealed class TestPathProvider(string path) : IPathProvider
{
    public string GetLyricsBasePath() => path;
}