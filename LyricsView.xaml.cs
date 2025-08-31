using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Text;

namespace PlexLyricSync;

public sealed partial class LyricsView : UserControl
{
    public MainWindow? MainWindow { get; set; }

    internal readonly LyricsClient _lyrics = new();
    internal List<LrcLine>? _lrc;
    internal bool _hasSynced = false;
    private bool _plainActive = false;
    internal string _trackKey = "";
    internal int _curLyricIdx = -1;
    private int _scrollOffset = 0;
    private int _visibleLines = 3;
    internal string? _localPath;
    private readonly TextBlock[] _syncedBlocks;
    private readonly double[] _baseSizes = { 16, 18, 20, 22, 20, 18, 16 };
    private readonly double[] _baseOpacities = { 0.35, 0.45, 0.65, 1.0, 0.80, 0.55, 0.35 };

    public LyricsView()
    {
        this.InitializeComponent();

        _syncedBlocks = new[] { LyPrev3, LyPrev2, LyPrev1, LyCurr0, LyNext1, LyNext2, LyNext3 };

        LyPrev3.Tapped += async (_, __) => await SeekToRelativeAsync(-3);
        LyPrev2.Tapped += async (_, __) => await SeekToRelativeAsync(-2);
        LyPrev1.Tapped += async (_, __) => await SeekToRelativeAsync(-1);
        LyCurr0.Tapped += async (_, __) => await SeekToRelativeAsync(0);
        LyNext1.Tapped += async (_, __) => await SeekToRelativeAsync(+1);
        LyNext2.Tapped += async (_, __) => await SeekToRelativeAsync(+2);
        LyNext3.Tapped += async (_, __) => await SeekToRelativeAsync(+3);
        LySource.Tapped += OnLySourceTapped;
        LyUpdate.Click += OnLyUpdateClicked;
        SyncedPanel.PointerWheelChanged += OnSyncedScroll;
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
                _plainActive = false;
                _scrollOffset = 0;
                SyncedPanel.Visibility = Visibility.Visible;
                PlainScroll.Visibility = Visibility.Collapsed;
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
                _plainActive = true;
                SyncedPanel.Visibility = Visibility.Collapsed;
                PlainScroll.Visibility = Visibility.Visible;
                PlainText.Text = lyrics.plain;
                PlainScroll.ChangeView(null, 0, null);
            });
        }
    }

    /// <summary>
    /// Updates UI lyrics when there are no lyrics.
    /// Can optionally set a custom message to appear
    /// </summary>
    /// <param name="msg">custom message</param>
    internal void SetNoLyrics(string? msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _lrc = null;
            _hasSynced = false;
            _plainActive = false;
            SyncedPanel.Visibility = Visibility.Visible;
            PlainScroll.Visibility = Visibility.Collapsed;
            LyCurr0.Text = msg ?? "No lyrics found.";
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
            if (idx != _curLyricIdx)
            {
                _curLyricIdx = idx;
                _scrollOffset = 0;
            }
            UpdateSyncedLyricStack(_curLyricIdx + _scrollOffset);
        }
        else
        {
            _curLyricIdx = -1;
            if (!_plainActive)
            {
                UpdateNonSyncedLyricStack(_hasSynced ? "" : LyCurr0.Text ?? "");
            }
        }
    }

    /// <summary>
    /// Updates the lyric stack when synced lyrics exist. It displays the current lyric line on LyCurr0 as well as the previous 3 and next 3 lines.
    /// </summary>
    /// <param name="idx">the current lyric index</param>
    internal void UpdateSyncedLyricStack(int idx)
    {
        string L(int i) => (_lrc is not null && i >= 0 && i < _lrc.Count) ? _lrc[i].Text : string.Empty;

        LyPrev3.Text = L(idx - 3);
        LyPrev2.Text = L(idx - 2);
        LyPrev1.Text = L(idx - 1);
        LyCurr0.Text = L(idx);
        LyNext1.Text = L(idx + 1);
        LyNext2.Text = L(idx + 2);
        LyNext3.Text = L(idx + 3);

        for (int i = 0; i < _syncedBlocks.Length; i++)
        {
            var tb = _syncedBlocks[i];
            tb.FontSize = _baseSizes[i];
            tb.Opacity = _baseOpacities[i];
            tb.FontWeight = FontWeights.Normal;
        }

        int curPos = _curLyricIdx - idx;
        if (curPos >= -3 && curPos <= 3)
        {
            var highlight = _syncedBlocks[curPos + 3];
            highlight.FontSize = 22;
            highlight.Opacity = 1.0;
            highlight.FontWeight = FontWeights.SemiBold;
        }
    }

    /// <summary>
    /// Updates the lyric stack when no synced lyrics exist. It displays the lyrics on LyCurr0.
    /// </summary>
    /// <param name="lyrics">lyrics to use</param>
    private void UpdateNonSyncedLyricStack(string lyrics)
    {
        LyPrev3.Text = LyPrev2.Text = LyPrev1.Text =
        LyNext1.Text = LyNext2.Text = LyNext3.Text = string.Empty;
        LyCurr0.Text = lyrics;
    }

    private async Task SeekToRelativeAsync(int delta)
    {
        try
        {
            if (_lrc is null || _lrc.Count == 0) return;
            int baseIdx = _curLyricIdx + _scrollOffset;
            int targetIdx = baseIdx + delta;
            if (targetIdx < 0 || targetIdx >= _lrc.Count) return;

            int targetMs = (int)_lrc[targetIdx].T.TotalMilliseconds;
            _curLyricIdx = targetIdx;
            _scrollOffset = 0;
            UpdateSyncedLyricStack(_curLyricIdx);

            if (MainWindow is not null) await MainWindow.SeekToMsAsync(targetMs);
        }
        catch { }
    }

    private void OnSyncedScroll(object sender, PointerRoutedEventArgs e)
    {
        if (_lrc is null || _lrc.Count == 0) return;
        int delta = e.GetCurrentPoint(SyncedPanel).Properties.MouseWheelDelta;
        int dir = delta > 0 ? -1 : 1;
        int maxUp = -_curLyricIdx;
        int maxDown = _lrc.Count - 1 - _curLyricIdx;
        _scrollOffset = Math.Clamp(_scrollOffset + dir, maxUp, maxDown);
        UpdateSyncedLyricStack(_curLyricIdx + _scrollOffset);
        e.Handled = true;
    }

    internal void SetSyncedLineCount(int lines)
    {
        _visibleLines = Math.Clamp(lines, 0, 3);
        LyPrev3.Visibility = _visibleLines >= 3 ? Visibility.Visible : Visibility.Collapsed;
        LyPrev2.Visibility = _visibleLines >= 2 ? Visibility.Visible : Visibility.Collapsed;
        LyPrev1.Visibility = _visibleLines >= 1 ? Visibility.Visible : Visibility.Collapsed;
        LyNext1.Visibility = _visibleLines >= 1 ? Visibility.Visible : Visibility.Collapsed;
        LyNext2.Visibility = _visibleLines >= 2 ? Visibility.Visible : Visibility.Collapsed;
        LyNext3.Visibility = _visibleLines >= 3 ? Visibility.Visible : Visibility.Collapsed;

        if (_hasSynced && _lrc is not null)
            UpdateSyncedLyricStack(_curLyricIdx + _scrollOffset);
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