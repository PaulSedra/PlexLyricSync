using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace PlexLyricSync;

public sealed partial class MainWindow : Window
{
    // TODO: set these
    private string PlexBaseUrl;
    private string PlexToken;

    private PlexApiClient? _plex;
    private CancellationTokenSource? _pollCts;

    // Latest metadata (from Plex)
    private string _artist = "", _title = "", _srvState = "";
    private int _srvDurMs = 0, _srvPosMs = 0;
    // For change detection
    private string _lastArtist = "", _lastTitle = "";
    private int _lastDurMs = 0;

    private DateTime _srvStampUtc = DateTime.UtcNow;

    private LyricsClient? _lyrics;
    private List<LrcLine>? _lrc;          // parsed synced lyrics
    private bool _hasSynced = false;
    private string _trackKey = "";        // to know when to (re)fetch

    // Display clock (predicted position between server ticks)
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public MainWindow()
    {
        InitializeComponent();

        var secrets = SecretsLoader.LoadSecrets();
        PlexBaseUrl = secrets.PlexBaseUrl;
        PlexToken = secrets.PlexToken;

        NowPlaying.Text = "Connecting to Plex…";

        this.Closed += (_, __) =>
        {
            _pollCts?.Cancel();
            _plex?.Dispose();
            _uiTimer.Stop();
        };

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _plex = new PlexApiClient(PlexBaseUrl, PlexToken);
        _lyrics = new LyricsClient();

        // Start UI prediction (keeps the bar moving smoothly between Plex updates)
        _uiTimer.Tick += (_, __) => UpdateProgressFromPrediction();
        _uiTimer.Start();

        // Kick an immediate poll and the background poll loop
        _pollCts = new CancellationTokenSource();
        await PollPlexOnceAsync(_pollCts.Token);
        _ = RunPollLoopAsync(_pollCts.Token);
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        // Poll ~3–4 times per second
        using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(300));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await PollPlexOnceAsync(ct);
        }
    }

    private async Task PollPlexOnceAsync(CancellationToken ct)
    {
        try
        {
            var np = await _plex!.GetPlexampNowPlayingAsync(ct);

            if (np is null)
            {
                _artist = _title = _srvState = "";
                _srvDurMs = 0;
                _lastArtist = _lastTitle = "";
                _lastDurMs = 0;

                DispatcherQueue.TryEnqueue(() => NowPlaying.Text = "Nothing playing");
                return;
            }

            // Always refresh labels & duration
            _artist = np.Artist;
            _title = np.Title;
            _srvDurMs = np.DurationMs;

            // ---- change detection (compute BEFORE assigning _srvState) ----
            var newState = np.State ?? "";
            bool trackChanged = _artist != _lastArtist || _title != _lastTitle || _srvDurMs != _lastDurMs;
            bool stateChanged = !string.Equals(newState, _srvState, StringComparison.OrdinalIgnoreCase);
            bool serverMoved = Math.Abs(np.ViewOffsetMs - _srvPosMs) > 1000; // >1s tick/seek

            if (trackChanged || stateChanged || serverMoved)
            {
                // Rebase snapshot on any meaningful change
                _srvPosMs = np.ViewOffsetMs;
                _srvStampUtc = DateTime.UtcNow;

                // Remember last known track to detect future changes
                _lastArtist = _artist;
                _lastTitle = _title;
                _lastDurMs = _srvDurMs;

                // Build a simple key; can switch to ratingKey later
                _trackKey = $"{_artist}|{_title}|{_srvDurMs}";
                _ = FetchLyricsAsync(_artist, _title, _trackKey, ct);
            }

            // Now commit the new state (after we've used the old one to detect change)
            _srvState = newState;

            // Update label (progress is driven by UpdateProgressFromPrediction)
            DispatcherQueue.TryEnqueue(() =>
            {
                NowPlaying.Text = (!string.IsNullOrWhiteSpace(_artist) || !string.IsNullOrWhiteSpace(_title))
                    ? $"{_artist} — {_title}"
                    : "Nothing playing";
            });
        }
        catch
        {
            // ignore transient errors
        }
    }

    private async Task FetchLyricsAsync(string artist, string title, string key, CancellationToken ct)
    {
        try
        {
            if (_lyrics is null) return;
            var res = await _lyrics.GetAsync(title, artist, ct);
            if (res is null) { SetNoLyrics("No lyrics found."); return; }

            if (!string.IsNullOrWhiteSpace(res.Value.syncedLrc))
            {
                var parsed = LrcParser.Parse(res.Value.syncedLrc!);
                DispatcherQueue.TryEnqueue(() =>
                {
                    // ensure still same track
                    if (key != _trackKey) return;
                    _lrc = parsed;
                    _hasSynced = _lrc.Count > 0;
                    LyricLine.Text = _hasSynced ? "…" : "No synced lyrics.";
                });
            }
            else if (!string.IsNullOrWhiteSpace(res.Value.plain))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (key != _trackKey) return;
                    _lrc = null; _hasSynced = false;
                    LyricLine.Text = res.Value.plain;  // unsynced: show all
                });
            }
            else
            {
                SetNoLyrics("No lyrics found.");
            }
        }
        catch
        {
            SetNoLyrics("No lyrics.");
        }
    }

    private void SetNoLyrics(string msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _lrc = null; _hasSynced = false;
            LyricLine.Text = msg;
        });
    }


    private void UpdateProgressFromPrediction()
    {
        if (_srvDurMs <= 0)
        {
            SongProgress.Value = 0;
            TimeLabel.Text = "00:00 / 00:00";
            return;
        }

        // Predict current position from last server sample.
        var elapsed = (DateTime.UtcNow - _srvStampUtc).TotalMilliseconds;
        double predicted = _srvState.Equals("playing", StringComparison.OrdinalIgnoreCase)
            ? _srvPosMs + Math.Max(0, elapsed)
            : _srvPosMs;

        // Clamp
        if (predicted < 0) predicted = 0;
        if (predicted > _srvDurMs) predicted = _srvDurMs;

        // Update progress bar
        SongProgress.Value = predicted / _srvDurMs * 100.0;

        // Format mm:ss / mm:ss
        TimeLabel.Text = $"{FormatTime(predicted)} / {FormatTime(_srvDurMs)}";

        // Update synced lyric line, if we have it
        if (_hasSynced && _lrc is not null && _lrc.Count > 0)
        {
            var idx = LrcParser.IndexAt(_lrc, TimeSpan.FromMilliseconds(predicted));
            if (idx >= 0 && idx < _lrc.Count)
                LyricLine.Text = _lrc[idx].Text;
        }
    }

    private static string FormatTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }
}
