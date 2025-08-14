using System;
using System.Collections.Generic;
using Xunit;

namespace PlexLyricSync.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parse_SingleLine_ReturnsTimestampAndText()
    {
        string lrc = "[01:23.45]Hello world";
        var result = LrcParser.Parse(lrc);
        Assert.Single(result);
        Assert.Equal(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(23) + TimeSpan.FromMilliseconds(450), result[0].T);
        Assert.Equal("Hello world", result[0].Text);
    }

    [Fact]
    public void Parse_UnsortedLines_SortsByTime()
    {
        string lrc = "[00:10]Second\n[00:05]First";
        var result = LrcParser.Parse(lrc);
        Assert.Equal(2, result.Count);
        Assert.Equal("First", result[0].Text);
        Assert.Equal("Second", result[1].Text);
    }

    [Fact]
    public void IndexAt_ReturnsLastIndexBeforeOrAtTime()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(5), "First"),
            new(TimeSpan.FromSeconds(10), "Second"),
        };

        Assert.Equal(-1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(7)));
        Assert.Equal(1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, LrcParser.IndexAt(lines, TimeSpan.FromSeconds(11)));
    }
}
