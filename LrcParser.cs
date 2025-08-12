using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PlexLyricSync;

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
