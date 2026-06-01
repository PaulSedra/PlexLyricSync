using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kawazu;
using PlexLyricSync.Core.Models;
using PlexLyricSync.Core.Utils;

namespace PlexLyricSync.Core.Lyrics;

public sealed class JapaneseTransliterator
{
    private const string RomanizedTitleSuffix = "_romanized";

    private static readonly Regex JapaneseText = new(
        @"[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff]",
        RegexOptions.Compiled);

    private readonly Lazy<KawazuConverter> _converter = new(CreateConverter);

    public static Task<LyricsFile?> GetLocalRomanizedLyricsAsync(
        string artist,
        string album,
        string title,
        CancellationToken ct) =>
        LrcParser.ReadLyricsAsync(artist, album, title + RomanizedTitleSuffix, ct);

    public static LyricsFile? WriteRomanizedLyrics(
        LyricsFile lyrics,
        string artist,
        string album,
        string title) =>
        LrcParser.WriteLyrics(lyrics, artist, album, title + RomanizedTitleSuffix);

    public static bool HasSameContent(LyricsFile? left, LyricsFile? right) =>
        string.Equals(left?.Synced, right?.Synced, StringComparison.Ordinal) &&
        string.Equals(left?.Plain, right?.Plain, StringComparison.Ordinal);

    public async Task<LyricsFile?> TransliterateAsync(LyricsFile lyrics, CancellationToken ct)
    {
        if (!ContainsJapanese(lyrics.Synced) && !ContainsJapanese(lyrics.Plain))
            return null;

        if (!string.IsNullOrWhiteSpace(lyrics.Synced))
        {
            List<SyncedLyricLine> parsed = LrcParser.Parse(lyrics.Synced);
            if (parsed.Count == 0)
                return null;

            List<string> transliterated = [];
            foreach (SyncedLyricLine line in parsed)
            {
                ct.ThrowIfCancellationRequested();
                transliterated.Add(await ToRomajiAsync(line.Text));
            }

            return new LyricsFile(
                LrcParser.CopyLrcTimeSpans(parsed, string.Join('\n', transliterated)),
                null,
                null);
        }

        if (string.IsNullOrWhiteSpace(lyrics.Plain))
            return null;

        string[] lines = lyrics.Plain.Replace("\r\n", "\n").Split('\n');
        string[] transliteratedLines = await Task.WhenAll(lines.Select(ToRomajiAsync));
        return new LyricsFile(null, string.Join(Environment.NewLine, transliteratedLines), null);
    }

    private static bool ContainsJapanese(string? text) =>
        !string.IsNullOrWhiteSpace(text) && JapaneseText.IsMatch(text);

    private async Task<string> ToRomajiAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !ContainsJapanese(text))
            return text;

        string romaji = await _converter.Value.Convert(
            text,
            To.Romaji,
            Mode.Spaced,
            RomajiSystem.Hepburn,
            string.Empty,
            string.Empty);

        return romaji.Trim();
    }

    private static KawazuConverter CreateConverter()
    {
        string outputDictionaryPath = Path.Combine(AppContext.BaseDirectory, "IpaDic");
        return Directory.Exists(outputDictionaryPath)
            ? new KawazuConverter(outputDictionaryPath)
            : new KawazuConverter();
    }
}
