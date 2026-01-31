using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PlexLyricSync.Utils;

public record LrcLine(TimeSpan T, string Text);

public static class LrcParser
{
    // [mm:ss] or [mm:ss.xx]
    private static readonly Regex Stamp = new(@"\[(\d{1,2}):(\d{2})(?:\.(\d{1,2}))?\](.*)", RegexOptions.Compiled);

    public static List<LrcLine> Parse(string lrc)
    {
        var lines = new List<LrcLine>();
        using var sr = new StringReader(lrc);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var m = Stamp.Match(line);
            if (!m.Success) continue;
            int mm = int.Parse(m.Groups[1].Value);
            int ss = int.Parse(m.Groups[2].Value);
            int cs = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(2, '0')) : 0; // centiseconds
            string text = m.Groups[4].Value.Trim();
            lines.Add(new LrcLine(new TimeSpan(0, 0, mm, ss, cs * 10), text));
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
    public static string CopyLrcTimeSpans(List<LrcLine> syncedLyrics, string nonSyncedLyrics)
    {
        var lyricsList = nonSyncedLyrics.Split('\n');

        // not sure if the following is actually needed
        // ensure line counts match to keep timestamps aligned
        // if (lyricsList.Length < syncedLyrics.Count)
        // {
        //     Array.Resize(ref lyricsList, syncedLyrics.Count);
        // }

        var lrcWriter = new System.Text.StringBuilder();
        for (int i = 0; i < syncedLyrics.Count; i++)
        {
            var line = syncedLyrics[i];
            var text = lyricsList.Length > i && !string.IsNullOrWhiteSpace(lyricsList[i])
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
    public static int IndexAt(IReadOnlyList<LrcLine> L, TimeSpan t)
    {
        int lo = 0, hi = L.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (L[mid].T <= t) { ans = mid; lo = mid + 1; } else { hi = mid - 1; }
        }
        return ans;
    }
}
