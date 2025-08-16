using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PlexLyricSync;

public sealed partial class LyricsView : UserControl
{
    internal readonly LyricsClient _lyrics = new();
    internal List<LrcLine>? _lrc;
    internal bool _hasSynced = false;
    internal string _trackKey = "";
    internal int _curLyricIdx = -1;
    internal string? _localPath;

    public Func<int, Task>? SeekToAsync { get; set; }

    public LyricsView()
    {
        this.InitializeComponent();

        LyPrev3.Tapped += async (_, __) => await SeekToRelativeAsync(-3);
        LyPrev2.Tapped += async (_, __) => await SeekToRelativeAsync(-2);
        LyPrev1.Tapped += async (_, __) => await SeekToRelativeAsync(-1);
        LyCurr0.Tapped += async (_, __) => await SeekToRelativeAsync(0);
        LyNext1.Tapped += async (_, __) => await SeekToRelativeAsync(+1);
        LyNext2.Tapped += async (_, __) => await SeekToRelativeAsync(+2);
        LyNext3.Tapped += async (_, __) => await SeekToRelativeAsync(+3);
        LySource.Tapped += OnLySourceTapped;
    }

    public async Task FetchLyricsAsync(string artist, string album, string title, string key, CancellationToken ct)
    {
        _trackKey = key;
        try
        {
            var res = await _lyrics.GetAsync(title, artist, album, ct);

            if (res is null)
            {
                SetNoLyrics("No lyrics found.");
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (key != _trackKey) return;
                    LySource.Text = string.Empty;
                });
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (key != _trackKey) return;
                if (res.Value.fromCache && !string.IsNullOrWhiteSpace(res.Value.path))
                {
                    _localPath = res.Value.path;
                    var idx = _localPath.IndexOf("lyrics", StringComparison.OrdinalIgnoreCase);
                    var disp = idx >= 0 ? _localPath[idx..].Replace('\\', '/') : _localPath;
                    LySource.Text = "Local";
                    LySource.ToolTip = disp;
                    LySource.Cursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                }
                else
                {
                    _localPath = null;
                    LySource.Text = "Remote";
                    LySource.ToolTip = null;
                    LySource.Cursor = null;
                }
            });

            if (!string.IsNullOrWhiteSpace(res.Value.syncedLrc))
            {
                var parsed = LrcParser.Parse(res.Value.syncedLrc!);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (key != _trackKey) return;
                    _lrc = parsed;
                    _hasSynced = true;
                });
            }
            else if (!string.IsNullOrWhiteSpace(res.Value.plain))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (key != _trackKey) return;
                    _lrc = null;
                    _hasSynced = false;
                    LyCurr0.Text = res.Value.plain;
                });
            }
            else
            {
                SetNoLyrics("No lyrics found.");
            }
        }
        catch
        {
            SetNoLyrics("Unable to fetch lyrics.");
        }
    }

    private void SetNoLyrics(string msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _lrc = null;
            _hasSynced = false;
            LyCurr0.Text = msg;
            LySource.Text = string.Empty;
            LySource.ToolTip = null;
            LySource.Cursor = null;
            _localPath = null;
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
            UpdateNonSyncedLyricStack(_hasSynced ? "" : LyCurr0.Text ?? "");
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
            int targetIdx = _curLyricIdx + delta;
            if (targetIdx < 0 || targetIdx >= _lrc.Count) return;

            int targetMs = (int)_lrc[targetIdx].T.TotalMilliseconds;
            _curLyricIdx = targetIdx;
            UpdateSyncedLyricStack(_curLyricIdx);

            if (SeekToAsync is not null)
            {
                await SeekToAsync(targetMs);
            }
        }
        catch { }
    }

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
}
