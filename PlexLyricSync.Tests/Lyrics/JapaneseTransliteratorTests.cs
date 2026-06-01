using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlexLyricSync.Core.Lyrics;
using PlexLyricSync.Core.Models;
using PlexLyricSync.Core.Utils;

namespace PlexLyricSync.Tests.Lyrics;

[TestClass]
public class JapaneseTransliteratorTests
{
    [TestMethod]
    public async Task TransliterateAsync_plainJapaneseLyrics_returnsRomaji()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "こんにちは", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("konnichiha", result.Plain);
    }

    [TestMethod]
    public async Task TransliterateAsync_syncedJapaneseLyrics_keepsTimestamps()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: "[00:01.00]こんにちは", Plain: null, Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        string synced = result.Synced!.Replace("\r\n", "\n").Trim();
        Assert.AreEqual("[00:01.00]konnichiha", synced);
    }

    [TestMethod]
    public async Task TransliterateAsync_kanjiHeavyLyrics_returnsSpacedRomaji()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "僕達は操り人形じゃない", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("bokutachi ha ayatsuri ningyou ja nai", result.Plain);
    }

    [TestMethod]
    public async Task TransliterateAsync_nonJapaneseLyrics_returnsNull()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "hello", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void WriteRomanizedLyrics_syncedSource_writesRomanizedLrc()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "PlexLyricSyncTests", Guid.NewGuid().ToString("N"));
        LrcParser.SetPathProvider(new TestPathProvider(testRoot));

        LyricsFile result = JapaneseTransliterator.WriteRomanizedLyrics(
            new LyricsFile("[00:01.00]konnichiha", null, null),
            artist: "Artist",
            album: "Album",
            title: "Title")!;

        Assert.IsTrue(result.Path!.EndsWith("Title_romanized.lrc", StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(result.Path));
    }

    [TestMethod]
    public void WriteRomanizedLyrics_plainSource_writesRomanizedTxt()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "PlexLyricSyncTests", Guid.NewGuid().ToString("N"));
        LrcParser.SetPathProvider(new TestPathProvider(testRoot));

        LyricsFile result = JapaneseTransliterator.WriteRomanizedLyrics(
            new LyricsFile(null, "konnichiha", null),
            artist: "Artist",
            album: "Album",
            title: "Title")!;

        Assert.IsTrue(result.Path!.EndsWith("Title_romanized.txt", StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(result.Path));
    }
}
