using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        LyUpdate.Click += OnLyUpdateClicked;
    }

    public async Task FetchLyricsAsync(string artist, string album, string title, string key, CancellationToken ct)
    {
        _trackKey = key;
        LyUpdate.Visibility = Visibility.Collapsed;

        try
        {
            var local = await _lyrics.GetLocalAsync(title, artist, album, ct);
            string? localText = null;
            if (local is not null)
            {
                localText = local.Value.syncedLrc ?? local.Value.plain;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (key != _trackKey) return;
                    _localPath = local.Value.path;
                    var idx = _localPath!.IndexOf("lyrics", StringComparison.OrdinalIgnoreCase);
                    var disp = idx >= 0 ? _localPath[idx..].Replace('\\', '/') : _localPath;
                    LySource.Text = "Local";
                    ToolTipService.SetToolTip(LySource, disp);
                });

                if (!string.IsNullOrWhiteSpace(local.Value.syncedLrc))
                {
                    var parsed = LrcParser.Parse(local.Value.syncedLrc);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (key != _trackKey) return;
                        _lrc = parsed;
                        _hasSynced = true;
                    });
                }
                else if (!string.IsNullOrWhiteSpace(local.Value.plain))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (key != _trackKey) return;
                        _lrc = null;
                        _hasSynced = false;
                        LyCurr0.Text = local.Value.plain;
                    });
                }
            }
            else
            {
                SetNoLyrics("No lyrics found.");
            }

            var remote = await _lyrics.GetRemoteAsync(title, artist, album, ct);
            if (!string.IsNullOrWhiteSpace(remote.syncedLrc) || !string.IsNullOrWhiteSpace(remote.plain))
            {
                var remoteText = remote.syncedLrc ?? remote.plain;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _localPath = remote.path;
                });

                if (local is null)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (key != _trackKey) return;
                        LySource.Text = "Remote";
                        ToolTipService.SetToolTip(LySource, null);
                    });

                    if (!string.IsNullOrWhiteSpace(remote.syncedLrc))
                    {
                        var parsed = LrcParser.Parse(remote.syncedLrc);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (key != _trackKey) return;
                            _lrc = parsed;
                            _hasSynced = true;
                        });
                    }
                    else if (!string.IsNullOrWhiteSpace(remote.plain))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (key != _trackKey) return;
                            _lrc = null;
                            _hasSynced = false;
                            LyCurr0.Text = remote.plain;
                        });
                    }
                }
                else if (remoteText != localText)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (key != _trackKey) return;
                        LyUpdate.Visibility = Visibility.Visible;
                    });
                }
            }
            else if (local is null)
            {
                SetNoLyrics("No lyrics found.");
            }
        }
        catch
        {
            if (_lrc is null)
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

    private async void OnLyUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_localPath)) return;
        try
        {
            var text = await File.ReadAllTextAsync(_localPath);
            if (_localPath.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = LrcParser.Parse(text);
                _lrc = parsed;
                _hasSynced = true;
            }
            else
            {
                _lrc = null;
                _hasSynced = false;
                LyCurr0.Text = text;
            }
            LyUpdate.Visibility = Visibility.Collapsed;
            var idx = _localPath.IndexOf("lyrics", StringComparison.OrdinalIgnoreCase);
            var disp = idx >= 0 ? _localPath[idx..].Replace('\\', '/') : _localPath;
            LySource.Text = "Local";
            ToolTipService.SetToolTip(LySource, disp);
        }
        catch { }
    }
}
