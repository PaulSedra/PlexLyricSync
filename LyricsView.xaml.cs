using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;

namespace PlexLyricSync;

public sealed partial class LyricsView : UserControl
{
    public MainWindow? MainWindow { get; set; }

    internal readonly LyricsClient _lyrics = new();
    internal List<LrcLine>? _lrc;
    internal bool _hasSynced = false;
    internal string _trackKey = "";
    internal int _curLyricIdx = -1;
    internal string? _localPath;

    private readonly List<TextBlock> _prevBlocks = new();
    private readonly List<TextBlock> _nextBlocks = new();
    private TextBlock _currBlock = new();
    internal int SyncedLineCount = 3;

    public LyricsView()
    {
        this.InitializeComponent();

        LySource.Tapped += OnLySourceTapped;
        LyUpdate.Click += OnLyUpdateClicked;

        ConfigureLyricBlocks();
    }

    internal void SetSyncedLineCount(int count)
    {
        SyncedLineCount = count;
        ConfigureLyricBlocks();
    }

    private void ConfigureLyricBlocks()
    {
        LyStack.Children.Clear();
        _prevBlocks.Clear();
        _nextBlocks.Clear();

        for (int i = SyncedLineCount; i >= 1; i--)
        {
            var tb = CreateLineBlock(28 - (SyncedLineCount - i + 1) * 2, 1 - (SyncedLineCount - i + 1) * 0.15);
            int offset = -i;
            tb.Tapped += async (_, __) => await SeekToRelativeAsync(offset);
            LyStack.Children.Add(tb);
            _prevBlocks.Add(tb);
        }

        _currBlock = CreateLineBlock(28, 1, FontWeights.SemiBold);
        _currBlock.Tapped += async (_, __) => await SeekToRelativeAsync(0);
        LyStack.Children.Add(_currBlock);

        for (int i = 1; i <= SyncedLineCount; i++)
        {
            var tb = CreateLineBlock(28 - i * 2, 1 - i * 0.15);
            int offset = i;
            tb.Tapped += async (_, __) => await SeekToRelativeAsync(offset);
            LyStack.Children.Add(tb);
            _nextBlocks.Add(tb);
        }
    }

    private static TextBlock CreateLineBlock(double fontSize, double opacity, FontWeight? weight = null)
    {
        return new TextBlock
        {
            FontSize = Math.Max(12, fontSize),
            Foreground = new SolidColorBrush(Colors.White),
            Opacity = Math.Max(0, opacity),
            TextWrapping = TextWrapping.WrapWholeWords,
            TextAlignment = TextAlignment.Center,
            FontWeight = weight ?? FontWeights.Normal
        };
    }

    /// <summary>
    /// Gets local and remote lyrics. Prioritizes displaying local lyrics.
    /// If there is a discrepancy, local lyrics are overwritten and the user is prompted to refresh UI lyrics.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="trackKey">track trackKey</param>
    /// <param name="ct">cancellation token</param>
    public async Task FetchLyricsAsync(string artist, string album, string title, string trackKey, CancellationToken ct)
    {
        _trackKey = trackKey;
        LyUpdate.Visibility = Visibility.Collapsed;
        LyricsClient.LyricsData? local = null;

        try
        {
            local = await _lyrics.GetLocalAsync(title, artist, album, ct);  // get local lyrics
            if (local is not null) SetLyrics(trackKey, local, true);        // local lyrics found
            else SetNoLyrics("Searching for lyrics...");                    // local lyrics not found
        }
        catch
        {
            if (_lrc is null) SetNoLyrics("An error occurred while attempting to grab lyrics locally.");
        }

        try
        {
            var remote = await _lyrics.GetRemoteAsync(title, artist, album, ct);  // get remote lyrics
            if (local is null)
            {
                if (remote is not null) SetLyrics(trackKey, remote, false);       // remote lyrics found
                else SetNoLyrics("No lyrics found.");                             // no local or remote lyrics found

            }
            else if (remote != local)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (trackKey != _trackKey) return;
                    LyUpdate.Visibility = Visibility.Visible;
                });
            }
        }
        catch
        {
            // Known errors:
            // Exception thrown: 'System.Threading.Tasks.TaskCanceledException' in System.Private.CoreLib.dll
            // This maybe happening because something goes wrong in _lyrics.GetRemoteAsync or the method is called twice?

            if (_lrc is null) SetNoLyrics("An error occurred while searching for lyrics.");
        }
    }

    /// <summary>
    /// Updates UI lyrics.
    /// </summary>
    /// <param name="trackKey">track key</param>
    /// <param name="lyrics">track lyrics</param>
    /// <param name="local">true if lyrics are local</param>
    private void SetLyrics(string trackKey, LyricsClient.LyricsData lyrics, bool local)
    {
        // updates lyrics source
        DispatcherQueue.TryEnqueue(() =>
        {
            if (trackKey != _trackKey) return;
            _localPath = lyrics.path;
            var idx = _localPath!.IndexOf("lyrics", StringComparison.OrdinalIgnoreCase);
            var disp = idx >= 0 ? _localPath[idx..].Replace('\\', '/') : _localPath;
            LySource.Text = local? "Local" : "Remote";
            ToolTipService.SetToolTip(LySource, local? disp : null);
        });

        // update synced lyrics
        if (!string.IsNullOrWhiteSpace(lyrics.syncedLrc))
        {
            var parsed = LrcParser.Parse(lyrics.syncedLrc);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                _lrc = parsed;
                _hasSynced = true;
                UpdateSyncedLyricStack(0);
            });
        }

        // update plain lyrics
        else if (!string.IsNullOrWhiteSpace(lyrics.plain))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (trackKey != _trackKey) return;
                _lrc = null;
                _hasSynced = false;
                UpdateNonSyncedLyricStack(lyrics.plain);
            });
        }
    }

    /// <summary>
    /// Updates the lyric display with a message when no lyrics or data are available.
    /// </summary>
    /// <param name="msg">Message to display.</param>
    internal void SetNoLyrics(string? msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _lrc = null;
            _hasSynced = false;
            UpdateNonSyncedLyricStack(msg ?? "No lyrics found.");
            LySource.Text = string.Empty;
            ToolTipService.SetToolTip(LySource, null);
            _localPath = null;
            LyUpdate.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Display lyrics and update current index.
    /// </summary>
    /// <param name="predictedViewOffsetMs">current view offset position</param>
    public void UpdateProgress(int predictedViewOffsetMs)
    {
        if (_hasSynced && _lrc is not null && _lrc.Count > 0)
        {
            var idx = LrcParser.IndexAt(_lrc, TimeSpan.FromMilliseconds(predictedViewOffsetMs));
            _curLyricIdx = idx;
            UpdateSyncedLyricStack(idx);
        }
        else
        {
            _curLyricIdx = -1;
            UpdateNonSyncedLyricStack(_hasSynced ? "" : _currBlock.Text ?? "");
        }
    }

    /// <summary>
    /// Updates the lyric stack when synced lyrics exist. It displays the current lyric line and surrounding context.
    /// </summary>
    /// <param name="idx">the current lyric index</param>
    internal void UpdateSyncedLyricStack(int idx)
    {
        string L(int i) => (_lrc is not null && i >= 0 && i < _lrc.Count) ? _lrc[i].Text : string.Empty;

        for (int i = 0; i < _prevBlocks.Count; i++)
            _prevBlocks[i].Text = L(idx - (_prevBlocks.Count - i));

        _currBlock.Text = L(idx);

        for (int i = 0; i < _nextBlocks.Count; i++)
            _nextBlocks[i].Text = L(idx + i + 1);
    }

    /// <summary>
    /// Updates the lyric stack when no synced lyrics exist. It displays the lyrics in a single block.
    /// </summary>
    /// <param name="lyrics">lyrics to use</param>
    private void UpdateNonSyncedLyricStack(string lyrics)
    {
        foreach (var tb in _prevBlocks) tb.Text = string.Empty;
        foreach (var tb in _nextBlocks) tb.Text = string.Empty;
        _currBlock.Text = lyrics;
        LyScroll.ChangeView(0, 0, null);
    }

    private async Task SeekToRelativeAsync(int delta)
    {
        try
        {
            if (_lrc is null || _lrc.Count == 0) return;
            int targetIdx = _curLyricIdx + delta;
            if (targetIdx < 0 || targetIdx >= _lrc.Count) return;

            int targetMs = (int)_lrc[targetIdx].T.TotalMilliseconds;
            _curLyricIdx = targetIdx;
            UpdateSyncedLyricStack(_curLyricIdx);

            if (MainWindow is not null) await MainWindow.SeekToMsAsync(targetMs);
        }
        catch { }
    }

    /// <summary>
    /// Opens explorer when LySource is clicked and LySource.Text = "Local".
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnLySourceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_localPath)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_localPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    /// <summary>
    /// Refreshes UI lyrics with local lyrics.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void OnLyUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_localPath)) return;
        try
        {
            var lyrics = await File.ReadAllTextAsync(_localPath);
            LyricsClient.LyricsData local = _localPath.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                ? new LyricsClient.LyricsData(lyrics, null, _localPath)
                : new LyricsClient.LyricsData(null, lyrics, _localPath);
            SetLyrics(_trackKey, local, true);

            LyUpdate.Visibility = Visibility.Collapsed;
        }
        catch { }
    }
}