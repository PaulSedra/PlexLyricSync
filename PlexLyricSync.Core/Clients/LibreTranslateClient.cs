using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using PlexLyricSync.Core.Utils;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Core.Clients;

public sealed class LibreTranslateClient(string libreTranslateBaseUrl)
{
    private static readonly HttpClient Http = Clients.Http.Client;
    private readonly string _libreTranslateBaseUrl = $"{libreTranslateBaseUrl.TrimEnd('/')}/translate";

    /// <summary>
    /// Translated any text to the specified target language.
    /// </summary>
    /// <param name="text">text to translate</param>
    /// <param name="targetLanguage">language to translate to</param>
    /// <param name="ct">cancellation token</param>
    /// <param name="sourceLanguage">(optional) language to translate from</param>
    /// <returns>string (optional): translated text</returns>
    private async Task<string?> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct, string sourceLanguage = "auto")
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var payload = new
        {
            q = text,
            source = sourceLanguage,
            target = targetLanguage,
            format = "text"
        };

        try
        {
            using HttpResponseMessage resp = await Http.PostAsJsonAsync(_libreTranslateBaseUrl, payload, ct);
            resp.EnsureSuccessStatusCode();
            LibreTranslateResponse? doc = await resp.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: ct);
            return doc!.TranslatedText;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    /// <summary>
    /// Grabs a cached translated lyrics file if it exists.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="targetLanguage">target language for translation</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>Lyrics Data (optional): local translated lyrics if found</returns>
    public static async Task<Lyrics?> GetLocalTranslationAsync(string artist, string album, string title, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage)) return null;
        return await LrcParser.ReadLyricsAsync(artist, album, title + "_" + targetLanguage, ct);
    }


    /// <summary>
    /// Translates the provided lyrics using LibreTranslate.
    /// </summary>
    /// <param name="lyrics">original lyrics data</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="targetLanguage">target language for translation</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>LyricsData (optional): remote translated lyrics if found</returns>
    public async Task<Lyrics?> GetRemoteTranslationAsync(Lyrics lyrics, string artist, string album, string title, string targetLanguage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage)) return null;

        Lyrics? translatedLyrics = null;

        LibreTranslateClient libreTranslateClient = new(_libreTranslateBaseUrl);

        // synced lyrics translation
        if (!string.IsNullOrWhiteSpace(lyrics.Synced))
        {
            var parsed = LrcParser.Parse(lyrics.Synced);
            if (parsed.Count == 0) return null;

            string[] originalLines = parsed.Select(l => l.Text).ToArray();
            string joined = string.Join("\n", originalLines);
            string? translated = await libreTranslateClient.TranslateTextAsync(joined, targetLanguage, ct);

            translatedLyrics = new Lyrics(LrcParser.CopyLrcTimeSpans(parsed, translated!), null, null);
        }
        // plain lyrics translation
        else if (!string.IsNullOrWhiteSpace(lyrics.Plain))
        {
            translatedLyrics = new Lyrics(null, await libreTranslateClient.TranslateTextAsync(lyrics.Plain, targetLanguage, ct), null);
        }

        return LrcParser.WriteLyrics(translatedLyrics!, artist, album, title + "_" + targetLanguage);
    }
}