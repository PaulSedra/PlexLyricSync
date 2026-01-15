using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync;

public sealed class LibreTranslateClient(string libreTranslateBaseUrl)
{
    private static readonly HttpClient _http = Http.Client;
    private readonly string _libreTranslateBaseUrl = $"{libreTranslateBaseUrl.TrimEnd('/')}/translate";

    /// <summary>
    /// Translated any text to the specified target language.
    /// </summary>
    /// <param name="text">text to translate</param>
    /// <param name="targetLanguage">language to translate to</param>
    /// <param name="ct">cancellation token</param>
    /// <param name="sourceLanguage">(optional) language to translate from</param>
    /// <returns>string (optional): translated text</returns>
    internal async Task<string?> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct, string sourceLanguage = "auto")
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
            using var resp = await _http.PostAsJsonAsync(_libreTranslateBaseUrl, payload, ct);
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: ct);
            return doc!.TranslatedText;
        }
        catch { }

        return null;
    }
}