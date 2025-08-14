using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace PlexLyricSync;

public sealed partial class MainWindow : Window
{
    private const int startWidth = 480, startHeight = 720;

    private string PlexBaseUrl;
    private string PlexToken;

    private PlexApiClient? _plex;
    private CancellationTokenSource? _pollCts;

    // latest plex metadata
    private string _artist = "", _title = "", _state = "";
    private int _durationMs = 0;
    private int _viewOffsetMs = 0;

    // prediction
    private int _predictedViewOffsetMs = 0;
    private DateTime _predictedViewOffsetUtc = DateTime.UtcNow;

    // lyrics
    private LyricsClient? _lyrics;
    private List<LrcLine>? _lrc;          // parsed synced lyrics
    private bool _hasSynced = false;
    private string _trackKey = "";        // to know when to (re)fetch

    // seeking
    private string _clientId = "";       // Plex player's machineIdentifier
    private int _curLyricIdx = -1;       // current lyric index for click seeking

    // Display clock (predicted position between server ticks)
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();

        this.AppWindow.Resize(new SizeInt32(startWidth, startHeight));
        if (this.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        // plex url and token from secerets file
        var secrets = SecretsLoader.LoadSecrets();
        PlexBaseUrl = secrets.PlexBaseUrl;
        PlexToken = secrets.PlexToken;

        NowPlaying.Text = "Connecting to Plex";

        // seek by lyric line
        LyPrev3.Tapped += (_, __) => _ = SeekToRelativeAsync(-3);
        LyPrev2.Tapped += (_, __) => _ = SeekToRelativeAsync(-2);
        LyPrev1.Tapped += (_, __) => _ = SeekToRelativeAsync(-1);
        LyCurr0.Tapped += (_, __) => _ = SeekToRelativeAsync(0);
        LyNext1.Tapped += (_, __) => _ = SeekToRelativeAsync(+1);
        LyNext2.Tapped += (_, __) => _ = SeekToRelativeAsync(+2);
        LyNext3.Tapped += (_, __) => _ = SeekToRelativeAsync(+3);

        this.Closed += (_, __) =>
        {
            _pollCts?.Cancel();
            _plex?.Dispose();
            _uiTimer.Stop();
        };

        this.KeyDown += MainWindow_KeyDown;

        PlayPauseButton.Click += PlayPauseButton_Click;

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
        // poll once per second
        using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(1000));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await PollPlexOnceAsync(ct);
        }
    }

    private async Task PollPlexOnceAsync(CancellationToken ct)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            var np = await _plex!.GetPlexampNowPlayingAsync(ct);

            if (np is null)
            {
                _artist = _title = _state = "";
                _durationMs = 0;
                _viewOffsetMs = 0;

                _predictedViewOffsetMs = 0;
                _predictedViewOffsetUtc = now;

                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                return;
            }

            // Capture client id for control (seek)
            _clientId = np.ClientId ?? _clientId;

            // change detection
            bool trackChanged = np.Artist != _artist || np.Title != _title || np.DurationMs != _durationMs;
            bool stateChanged = !np.State.Equals(_state, StringComparison.OrdinalIgnoreCase);
            bool viewOffsetChanged = np.ViewOffsetMs != _viewOffsetMs;

            if (trackChanged || stateChanged || viewOffsetChanged)
            {
                // track
                _artist = np.Artist;
                _title = np.Title;
                _durationMs = np.DurationMs;
                _state = np.State;

                // viewOffset
                _viewOffsetMs = np.ViewOffsetMs;

                // prediction
                _predictedViewOffsetMs = _viewOffsetMs;
                _predictedViewOffsetUtc = now;
            }

            if (trackChanged)
            {
                _curLyricIdx = -1;
                _trackKey = $"{_artist}|{_title}|{_durationMs}";
                await FetchLyricsAsync(_artist, _title, _trackKey, ct).ConfigureAwait(false);
            }

            // Update labels
            DispatcherQueue.TryEnqueue(UpdateTrackInformation);
        }
        catch
        {}
    }

    private async Task FetchLyricsAsync(string artist, string title, string key, CancellationToken ct)
    {
        try
        {
            if (_lyrics is null) return;
            var res = await _lyrics.GetAsync(title, artist, ct);
            
            if (res is null) {
                SetNoLyrics("No lyrics found.");
                return;
            }

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
            _lrc = null; _hasSynced = false;
            LyCurr0.Text = msg;
        });
    }

    private void UpdateProgressFromPrediction()
    {
        if (_state == "paused") return;
        if (_durationMs <= 0)
        {
            UpdateTrackInformation();
            return;
        }

        DateTime now = DateTime.UtcNow;
        int elapsed = (int)Math.Ceiling((now - _predictedViewOffsetUtc).TotalMilliseconds);
        _predictedViewOffsetMs = _state.Equals("playing", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(_predictedViewOffsetMs + Math.Max(0, elapsed), 0, _durationMs)
            : _predictedViewOffsetMs; ;
        _predictedViewOffsetUtc = now;

        UpdateTrackInformation();

        // display lyrics (and capture current index for seeking)
        if (_hasSynced && _lrc is not null && _lrc.Count > 0)
        {
            var idx = LrcParser.IndexAt(_lrc, TimeSpan.FromMilliseconds(_predictedViewOffsetMs));
            _curLyricIdx = idx; // keep for clicks
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
    private void UpdateSyncedLyricStack(int idx)
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
        // TODO this method should just be able to read _lrc directly
        LyPrev3.Text = LyPrev2.Text = LyPrev1.Text =
        LyNext1.Text = LyNext2.Text = LyNext3.Text = string.Empty;
        LyCurr0.Text = lyrics;
    }

    private async Task SeekToRelativeAsync(int delta)
    {
        try
        {
            if (_plex is null || _lrc is null || _lrc.Count == 0) return;
            if (string.IsNullOrWhiteSpace(_clientId)) return;
            if (_curLyricIdx < 0) return;

            int targetIdx = _curLyricIdx + delta;
            if (targetIdx < 0 || targetIdx >= _lrc.Count) return;

            int targetMs = (int)_lrc[targetIdx].T.TotalMilliseconds;

            // ask Plex to seek (works even if paused; it stays paused at new position)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var ok = await _plex.SeekToAsync(_clientId, targetMs, cts.Token);
            if (!ok) return;

            // Rebase prediction lane immediately (instant UI response)
            _predictedViewOffsetMs = targetMs;
            _predictedViewOffsetUtc = DateTime.UtcNow;
            // keep _predState as-is (respect pause/play)

            // Move lyrics & progress IMMEDIATELY (no waiting for next tick)
            _curLyricIdx = targetIdx; // jump to the clicked line
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateTrackInformation();
                // lyric stack
                if (_hasSynced && _lrc is not null && _lrc.Count > 0)
                    UpdateSyncedLyricStack(_curLyricIdx);
            });
        }
        catch
        {}
    }

    /// <summary>
    /// Updates ArtistBlock, NowPlaying, SongProgress, and TimeLabel
    /// </summary>
    private void UpdateTrackInformation()
    {
        ArtistBlock.Text = !string.IsNullOrWhiteSpace(_artist) ? _artist : "";
        NowPlaying.Text = !string.IsNullOrWhiteSpace(_title) ? _title : "Peace and quiet";
        SongProgress.Value = _durationMs == 0
            ? 0
            : Math.Clamp((double)_predictedViewOffsetMs / _durationMs * 100.0, 0, 100);
        TimeLabel.Text = $"{FormatTime(_predictedViewOffsetMs)} / {FormatTime(_durationMs)}";
        PlayPauseButton.Content = _state.Equals("playing", StringComparison.OrdinalIgnoreCase) ? "Pause" : "Play";
    }

    private static string FormatTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    private async void MainWindow_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            await TogglePlayPauseAsync();
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        await TogglePlayPauseAsync();
    }

    private async Task TogglePlayPauseAsync()
    {
        try
        {
            if (_plex is null || string.IsNullOrWhiteSpace(_clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            bool ok;
            if (_state.Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                UpdateProgressFromPrediction();
                ok = await _plex.PauseAsync(_clientId, cts.Token);
                if (!ok) return;
                _state = "paused";
            }
            else
            {
                ok = await _plex.PlayAsync(_clientId, cts.Token);
                if (!ok) return;
                _state = "playing";
                _predictedViewOffsetUtc = DateTime.UtcNow;
            }

            DispatcherQueue.TryEnqueue(UpdateTrackInformation);
        }
        catch
        {
        }
    }
}
