using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    // seeking
    private string _clientId = "";       // Plex player's machineIdentifier
    private bool _isSeeking = false;     // true while user is dragging the progress bar

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

        LyricsView.SeekToAsync = SeekToMsAsync;

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
                var trackKey = $"{_artist}|{_title}|{_durationMs}";
                await LyricsView.FetchLyricsAsync(_artist, _title, trackKey, ct).ConfigureAwait(false);
            }

            // Update labels
            DispatcherQueue.TryEnqueue(UpdateTrackInformation);
        }
        catch
        { }
    }

    private void UpdateProgressFromPrediction()
    {
        if (_durationMs <= 0)
        {
            UpdateTrackInformation();
            LyricsView.UpdateProgress(0);
            return;
        }

        DateTime now = DateTime.UtcNow;
        int elapsed = (int)Math.Ceiling((now - _predictedViewOffsetUtc).TotalMilliseconds);
        _predictedViewOffsetMs = _state.Equals("playing", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(_predictedViewOffsetMs + Math.Max(0, elapsed), 0, _durationMs)
            : _predictedViewOffsetMs;
        _predictedViewOffsetUtc = now;

        UpdateTrackInformation();
        LyricsView.UpdateProgress(_predictedViewOffsetMs);
    }

    /// <summary>
    /// Handles song progress bar drag behavior.
    /// </summary>
    /// <param name="progressBar">the progress bar element</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async Task SeekFromPointerAsync(FrameworkElement progressBar, PointerRoutedEventArgs pointerEvent)
    {
        try
        {
            if (_plex is null || string.IsNullOrWhiteSpace(_clientId)) return;
            if (_durationMs <= 0) return;

            double x = pointerEvent.GetCurrentPoint(progressBar).Position.X;
            double fraction = progressBar.ActualWidth > 0 ? x / progressBar.ActualWidth : 0;
            fraction = Math.Clamp(fraction, 0, 1);
            int targetMs = (int)(_durationMs * fraction);

            if (_isSeeking)
            {
                _predictedViewOffsetMs = targetMs;
                _predictedViewOffsetUtc = DateTime.UtcNow;
                LyricsView.UpdateProgress(targetMs);
                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
            }
            else
            {
                await SeekToMsAsync(targetMs);
            }
        }
        catch
        { }
    }

    private async Task SeekToMsAsync(int targetMs)
    {
        try
        {
            if (_plex is null || string.IsNullOrWhiteSpace(_clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var ok = await _plex.SeekToAsync(_clientId, targetMs, cts.Token);
            if (!ok) return;

            _predictedViewOffsetMs = targetMs;
            _predictedViewOffsetUtc = DateTime.UtcNow;

            LyricsView.UpdateProgress(targetMs);
            DispatcherQueue.TryEnqueue(UpdateTrackInformation);
        }
        catch
        { }
    }

    /// <summary>
    /// Handles progress bar drag start.
    /// On drag start the app UI is updated but the plex client is not.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerPressed(object sender, PointerRoutedEventArgs pointerEvent)
    {
        _isSeeking = true;
        var progressBar = (FrameworkElement)sender;
        progressBar.CapturePointer(pointerEvent.Pointer);
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    /// <summary>
    /// Handles dragging across the progress bar.
    /// On drag the app UI is updated but the plex client is not.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerMoved(object sender, PointerRoutedEventArgs pointerEvent)
    {
        if (!_isSeeking) return;
        var progressBar = (FrameworkElement)sender;
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    /// <summary>
    /// Handles progress bar drag end.
    /// On drag end the plex client is updated.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerReleased(object sender, PointerRoutedEventArgs pointerEvent)
    {
        if (!_isSeeking) return;
        _isSeeking = false;
        var progressBar = (FrameworkElement)sender;
        progressBar.ReleasePointerCaptures();
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    /// <summary>
    /// Updates ArtistBlock, NowPlaying, SongProgress, and TimeLabel
    /// </summary>
    private void UpdateTrackInformation()
    {
        ArtistBlock.Text = !string.IsNullOrWhiteSpace(_artist) ? _artist : "";
        NowPlaying.Text = !string.IsNullOrWhiteSpace(_title) ? _title : "Peace and quiet";
        SongProgressFill.Width = _durationMs == 0
            ? 0
            : SongProgress.ActualWidth * Math.Clamp((double)_predictedViewOffsetMs / _durationMs, 0, 1);
        TimeLabel.Text = $"{FormatTime(_predictedViewOffsetMs)} / {FormatTime(_durationMs)}";
        PlayPauseIcon.Symbol = _state.Equals("playing", StringComparison.OrdinalIgnoreCase) ? Symbol.Pause : Symbol.Play;
    }

    private static string FormatTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
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
        {}
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(true);
        Root.Focus(FocusState.Programmatic);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(false);
        Root.Focus(FocusState.Programmatic);
    }

    private async Task SkipAsync(bool forward)
    {
        try
        {
            if (_plex is null || string.IsNullOrWhiteSpace(_clientId)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (forward)
                await _plex.SkipNextAsync(_clientId, cts.Token);
            else
                await _plex.SkipPreviousAsync(_clientId, cts.Token);
        }
        catch
        { }
    }
}
