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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;
using PlexLyricSync.Clients;
using PlexLyricSync.Utils;

namespace PlexLyricSync;

public sealed partial class LyricsView : UserControl
{
    public MainWindow? MainWindow { get; set; }

    internal LyricsClient.LyricsData _lyrics;
    internal List<LrcLine>? _lrc;
    private List<LrcLine>? _translatedLrc;
    internal string _trackKey = "";
    internal int _curLyricIdx = -1;
    private int _scrollOffset = 0;
    private int _visibleLines = 3;
    internal string? _localPath;
    private readonly List<TextBlock> _syncedBlocks = new();
    private string? _preferredLanguage;
    private bool _translationsEnabled = true;
    private bool _showTranslations = true;

    public LyricsView()
    {
        this.InitializeComponent();

        LySource.Tapped += OnLySourceTapped;
        LyUpdate.Click += OnLyUpdateClicked;
        SyncedPanel.PointerWheelChanged += OnSyncedScroll;

        SetSyncedLineCount(_visibleLines);
    }

    // TODO need better implementation
    internal void SetPreferredLanguage(string? languageCode)
    {
        _preferredLanguage = string.IsNullOrWhiteSpace(languageCode) ? null : languageCode;
    }

    internal void SetTranslationEnabled(bool enabled)
    {
        _translationsEnabled = enabled;
    }

    internal void SetShowTranslations(bool show)
    {
        _showTranslations = show;
    }

    private void RebuildSyncedBlocks()
    {
        SyncedPanel.Children.Clear();
        _syncedBlocks.Clear();

        for (int i = -_visibleLines; i <= _visibleLines; i++)
        {
            var tb = new TextBlock
            {
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.WrapWholeWords,
                TextAlignment = TextAlignment.Center
            };
            int rel = i;
            tb.Tapped += async (_, __) => await SeekToRelativeAsync(rel);
            _syncedBlocks.Add(tb);
            SyncedPanel.Children.Add(tb);
        }
    }

    private static double SizeFor(int dist) => Math.Max(22 - 2 * dist, 12);

    private static double OpacityFor(int dist) => dist == 0 ? 1.0 : Math.Max(0.8 - 0.25 * (dist - 1), 0.35);

    /// <summary>
    /// Gets local and remote lyrics. Prioritizes displaying local lyrics.
    /// If there is a discrepancy, local lyrics are overwritten and the user is prompted to refresh UI lyrics.
    /// </summary>
    /// <param name="artist">track artist</param>
    /// <param name="album">track album</param>
    /// <param name="title">track title</param>
    /// <param name="duration">track duration</param>
    /// <param name="trackKey">track trackKey</param>
    /// <param name="ct">cancellation token</param>
    public async Task FetchLyricsAsync(string artist, string album, string title, int duration, string trackKey, CancellationToken ct)
    {
        _trackKey = trackKey;
        LyUpdate.Visibility = Visibility.Collapsed;
        LyricsClient.LyricsData? local = null;

        try
        {
            local = await LyricsClient.GetLocalLyricsAsync(artist, album, title, ct);  // get local lyrics
            if (local is not null) SetLyrics(trackKey, local, true);                   // local lyrics found
            else SetNoLyrics("Searching for lyrics...");                               // local lyrics not found
        }
        catch
        {
            if (_lrc is null) SetNoLyrics("An error occurred while attempting to grab lyrics locally.");
        }

        try
        {
            var remote = await LyricsClient.GetRemoteLyricsAsync(artist, album, title, duration, ct);  // get remote lyrics
            if (local is null)
            {
                if (remote is not null) SetLyrics(trackKey, remote, false);                            // remote lyrics found
                else SetNoLyrics("No lyrics found.");                                                  // no local or remote lyrics found
            }

            // if remote lyrics are different from local lyrics display an option to refresh the displayed lyrics
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
            if (_lrc is null) SetNoLyrics("An error occurred while searching for lyrics.");
        }
    }

    public async Task FetchTranslatedLyricsAsync(string artist, string album, string title, string trackKey, CancellationToken ct)
    {
        // Translate lyrics in the background when enabled and a preferred language is set
        if (_lyrics is not null && _translationsEnabled && !string.IsNullOrWhiteSpace(_preferredLanguage))
        {
            try
            {
                var lang = _preferredLanguage;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Try to use cached translation first
                        var existing = await LyricsClient.GetLocalTranslationAsync(artist, album, title, lang!, ct);
                        var translated = existing ?? await LyricsClient.GetRemoteTranslationAsync(_lyrics, artist, album, title, lang!, ct);
                        if (translated is null) return;

                        if (!string.IsNullOrWhiteSpace(translated.syncedLrc))
                        {
                            var parsedTranslated = LrcParser.Parse(translated.syncedLrc);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (trackKey != _trackKey) return;
                                _translatedLrc = parsedTranslated;
                                if (!string.IsNullOrWhiteSpace(_lyrics.syncedLrc) && _lrc is not null)
                                {
                                    UpdateSyncedLyricStack(_curLyricIdx + _scrollOffset);
                                }
                            });
                        }
                        else if (!string.IsNullOrWhiteSpace(translated.plain))
                        {
                            var translatedPlain = translated.plain;
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (trackKey != _trackKey) return;
                                if (!string.IsNullOrWhiteSpace(_lyrics.plain))
                                {
                                    PlainText.Text = CombinePlainWithTranslation(_lyrics.plain, translated.plain, _showTranslations);
                                }
                            });
                        }
                    }
                    catch
                    {
                        // Ignore translation errors
                    }
                }, ct);
            }
            catch
            {
            }
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
        _lyrics = lyrics;
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
                _translatedLrc = null;
                _scrollOffset = 0;
                SyncedPanel.Visibility = Visibility.Visible;
                PlainScroll.Visibility = Visibility.Collapsed;
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
                _translatedLrc = null;
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
            _translatedLrc = null;
            SyncedPanel.Visibility = Visibility.Visible;
            PlainScroll.Visibility = Visibility.Collapsed;
            if (_syncedBlocks.Count == 0) SetSyncedLineCount(_visibleLines);
            _syncedBlocks[_visibleLines].Text = msg ?? "No lyrics found.";
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
        if (_lyrics is null) return;
        if (!string.IsNullOrWhiteSpace(_lyrics.syncedLrc) && _lrc is not null && _lrc.Count > 0)
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
            if (!string.IsNullOrWhiteSpace(_lyrics.plain))
            {
                var cur = _syncedBlocks.Count > _visibleLines ? _syncedBlocks[_visibleLines].Text : "";
                UpdateNonSyncedLyricStack(!string.IsNullOrWhiteSpace(_lyrics.syncedLrc) ? "" : cur);
            }
        }
    }

    /// <summary>
    /// Updates the lyric stack when synced lyrics exist. It displays the current lyric line along with surrounding lines.
    /// </summary>
    /// <param name="idx">the current lyric index</param>
    internal void UpdateSyncedLyricStack(int idx)
    {
        string L(int i) => (_lrc is not null && i >= 0 && i < _lrc.Count) ? _lrc[i].Text : string.Empty;
        string LT(int i) => (_translatedLrc is not null && i >= 0 && i < _translatedLrc.Count) ? _translatedLrc[i].Text : string.Empty;

        int highlightBlockIndex = -1;
        int curPos = _curLyricIdx - idx;
        if (curPos >= -_visibleLines && curPos <= _visibleLines)
        {
            highlightBlockIndex = curPos + _visibleLines;
        }

        for (int i = -_visibleLines; i <= _visibleLines; i++)
        {
            int idxBlock = i + _visibleLines;
            var tb = _syncedBlocks[idxBlock];
            var original = L(idx + i);
            var translated = _showTranslations ? LT(idx + i) : string.Empty;

            tb.Inlines.Clear();
            bool hasOriginal = !string.IsNullOrWhiteSpace(original);
            bool hasTranslated = !string.IsNullOrWhiteSpace(translated) && !string.Equals(original, translated, StringComparison.Ordinal);
            bool isHighlight = idxBlock == highlightBlockIndex;

            int dist = Math.Abs(i);
            double baseFontSize = SizeFor(dist);
            tb.FontSize = baseFontSize;
            tb.Opacity = OpacityFor(dist);
            tb.FontWeight = FontWeights.Normal;

            if (hasOriginal || !hasTranslated)
            {
                var origRun = new Run
                {
                    Text = original,
                    FontWeight = isHighlight ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = baseFontSize
                };
                tb.Inlines.Add(origRun);
            }

            if (hasTranslated)
            {
                tb.Inlines.Add(new LineBreak());
                var transRun = new Run
                {
                    Text = translated,
                    FontWeight = FontWeights.Normal,
                    FontSize = baseFontSize * 0.85,
                    Foreground = new SolidColorBrush(Colors.LightGray)
                };
                tb.Inlines.Add(transRun);
            }
        }
    }

    /// <summary>
    /// Updates the lyric stack when no synced lyrics exist. It displays the lyrics on the center line.
    /// </summary>
    /// <param name="lyrics">lyrics to use</param>
    private void UpdateNonSyncedLyricStack(string lyrics)
    {
        for (int i = 0; i < _syncedBlocks.Count; i++)
        {
            var tb = _syncedBlocks[i];
            tb.Text = string.Empty;
            int dist = Math.Abs(i - _visibleLines);
            tb.FontSize = SizeFor(dist);
            tb.Opacity = OpacityFor(dist);
            tb.FontWeight = FontWeights.Normal;
        }
        if (_syncedBlocks.Count > _visibleLines)
        {
            var center = _syncedBlocks[_visibleLines];
            center.Text = lyrics;
            center.FontSize = 22;
            center.Opacity = 1.0;
            center.FontWeight = FontWeights.SemiBold;
        }
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
        _visibleLines = Math.Max(0, lines);
        RebuildSyncedBlocks();

        if (_lyrics is null) return;
        if (!string.IsNullOrWhiteSpace(_lyrics.syncedLrc) && _lrc is not null)
            UpdateSyncedLyricStack(_curLyricIdx + _scrollOffset);
        else
            UpdateNonSyncedLyricStack(_syncedBlocks.Count > _visibleLines ? _syncedBlocks[_visibleLines].Text : "");
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
            _translatedLrc = null;

            LyUpdate.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private static string CombinePlainWithTranslation(string original, string translated, bool showTranslation)
    {
        if (!showTranslation || string.IsNullOrWhiteSpace(translated))
            return original;

        var originalLines = original.Replace("\r\n", "\n").Split('\n');
        var translatedLines = translated.Replace("\r\n", "\n").Split('\n');

        var sb = new System.Text.StringBuilder();
        int max = Math.Max(originalLines.Length, translatedLines.Length);
        for (int i = 0; i < max; i++)
        {
            var o = i < originalLines.Length ? originalLines[i] : string.Empty;
            var t = i < translatedLines.Length ? translatedLines[i] : string.Empty;
            if (!string.IsNullOrWhiteSpace(o))
            {
                sb.AppendLine(o);
            }
            if (!string.IsNullOrWhiteSpace(t) && !string.Equals(o, t, StringComparison.Ordinal))
            {
                sb.AppendLine(t);
            }
            if (i < max - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
