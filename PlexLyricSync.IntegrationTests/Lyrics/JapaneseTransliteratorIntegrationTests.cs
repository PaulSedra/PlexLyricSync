using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlexLyricSync.Core.Lyrics;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.IntegrationTests.Lyrics;

[TestClass]
public class JapaneseTransliteratorIntegrationTests
{
    [TestMethod]
    public async Task TransliterateAsync_kanjiLyrics_returnsRomaji()
    {
        JapaneseTransliterator transliterator = new();
        LyricsFile lyrics = new(Synced: null, Plain: "今晩は", Path: null);

        LyricsFile result = await transliterator.TransliterateAsync(lyrics, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("komban ha", result.Plain);
    }
}
