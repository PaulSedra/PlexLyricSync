using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Core.Utils;

public static class LrcParser
{
    // [mm:ss] or [mm:ss.xx]
    private static readonly Regex Stamp = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,2}))?\](.*)", RegexOptions.Compiled);


    /// <summary>
    /// Checks if string is a valid folder/file name and replaces invalid characters with "_".
    /// </summary>
    /// <param name="s">string to check</param>
    /// <returns>A valid folder/file name</returns>
    private static string Sanitize(string s)
    {
        return string.IsNullOrWhiteSpace(s)
            ? "Unknown"
            : Path.GetInvalidFileNameChars().Aggregate(s, (current, c) => current.Replace(c, '_'));
    }

    /// <summary>
    /// Creates lyrics directory if missing and returns the path.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <returns>Lyrics directory path</returns>
    private static string GetLyricsDirectory(string artist, string album)
    {
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        string directory = Path.Combine(music, "PlexLyricSync", "lyrics", Sanitize(artist), Sanitize(album));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Read a lyrics file from disk based on track information.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public static async Task<Lyrics?> ReadLyricsAsync(string artist, string album, string title, CancellationToken ct)
    {
        // file path
        string directory = GetLyricsDirectory(artist, album);
        string txtPath = Path.Combine(directory, Sanitize(title) + ".txt");
        string lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");

        if (File.Exists(lrcPath))
        {
            string lrc = await File.ReadAllTextAsync(lrcPath, ct);
            return new Lyrics(lrc, null, lrcPath);
        }

        if (File.Exists(txtPath))
        {
            string plain = await File.ReadAllTextAsync(txtPath, ct);
            return new Lyrics(null, plain, txtPath);
        }

        return null;
    }

    /// <summary>
    /// Writes a lyrics file to disk based on track information.
    /// </summary>
    /// <param name="lrcLibResponse">LRCLIB response to be written to disk</param>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <returns></returns>
    public static Lyrics? WriteLyrics(Lyrics lrcLibResponse, string artist, string album, string title)
    {
        // file path
        string directory = GetLyricsDirectory(artist, album);
        string lrcPath = Path.Combine(directory, Sanitize(title) + ".lrc");
        string txtPath = Path.Combine(directory, Sanitize(title) + ".txt");

        if (!string.IsNullOrWhiteSpace(lrcLibResponse.Synced))
        {
            File.WriteAllText(lrcPath, lrcLibResponse.Synced);
            return new Lyrics(lrcLibResponse.Synced, null, lrcPath);
        }

        if (!string.IsNullOrWhiteSpace(lrcLibResponse.Plain))
        {
            File.WriteAllText(txtPath, lrcLibResponse.Plain);
            return new Lyrics(null, lrcLibResponse.Plain, txtPath);
        }

        return null;
    }

    public static List<SyncedLyricLine> Parse(string lrc)
    {
        List<SyncedLyricLine> lines = [];
        using StringReader sr = new(lrc);
        while (sr.ReadLine() is { } line)
        {
            Match m = Stamp.Match(line);
            if (!m.Success) continue;
            int mm = int.Parse(m.Groups[1].Value);
            int ss = int.Parse(m.Groups[2].Value);
            int cs = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(2, '0')) : 0; // centiseconds
            string text = m.Groups[4].Value.Trim();
            lines.Add(new SyncedLyricLine(new TimeSpan(0, 0, mm, ss, cs * 10), text));
        }
        lines.Sort((a, b) => a.T.CompareTo(b.T));
        return lines;
    }

    /// <summary>
    /// Converts non-synced lyrics to synced lyrics using an existing list of lrc lines.
    /// </summary>
    /// <param name="syncedLyrics">the list of lrc lines to use in conversion</param>
    /// <param name="nonSyncedLyrics">the non-synced lyrics to convert</param>
    /// <returns>string representation of synced lyrics</returns>
    public static string CopyLrcTimeSpans(List<SyncedLyricLine> syncedLyrics, string nonSyncedLyrics)
    {
        string[] lyricsList = nonSyncedLyrics.Split('\n');

        // not sure if the following is actually needed
        // ensures line counts match to keep timestamps aligned
        // if (lyricsList.Length < syncedLyrics.Count)
        // {
        //     Array.Resize(ref lyricsList, syncedLyrics.Count);
        // }

        StringBuilder lrcWriter = new();
        for (int i = 0; i < syncedLyrics.Count; i++)
        {
            SyncedLyricLine line = syncedLyrics[i];
            string text = lyricsList.Length > i && !string.IsNullOrWhiteSpace(lyricsList[i])
                ? lyricsList[i]
                : syncedLyrics[i].Text;
            lrcWriter.Append('[');
            lrcWriter.AppendFormat("{0:D2}:{1:D2}.{2:D2}", line.T.Minutes, line.T.Seconds, line.T.Milliseconds / 10);
            lrcWriter.Append(']');
            lrcWriter.AppendLine(text);
        }

        return lrcWriter.ToString();
    }

    // last index whose time <= t, or -1
    public static int IndexAt(IReadOnlyList<SyncedLyricLine> l, TimeSpan t)
    {
        int lo = 0, hi = l.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (l[mid].T <= t) { ans = mid; lo = mid + 1; } else { hi = mid - 1; }
        }
        return ans;
    }
}
