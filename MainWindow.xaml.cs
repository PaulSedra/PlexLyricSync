using System;
using System.Net.Http;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using ColorThiefDotNet;
using System.Drawing;

namespace PlexLyricSync;

public sealed partial class MainWindow : Window
{
    private const int startWidth = 480, startHeight = 720;

    private string PlexBaseUrl;
    private string PlexToken;

    internal PlexApiClient? _plex;
    internal string _clientId = "";       // Plex player's machineIdentifier
    private CancellationTokenSource? _pollCts;
    private readonly HttpClient _artHttp = new();

    // latest plex metadata
    private string _artist = "", _album = "", _title = "", _artUrl = "";
    internal string _state = "";
    internal int _durationMs = 0;
    private int _viewOffsetMs = 0;

    // prediction
    internal int _predictedViewOffsetMs = 0;
    internal DateTime _predictedViewOffsetUtc = DateTime.UtcNow;

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

        // provide self reference for control callbacks
        ControlPanel.MainWindow = this;
        ControlPanel.LyricsView = LyricsView;

        // plex url and token from secerets file
        var secrets = SecretsLoader.LoadSecrets();
        PlexBaseUrl = secrets.PlexBaseUrl;
        PlexToken = secrets.PlexToken;
        _artHttp.DefaultRequestHeaders.TryAddWithoutValidation("X-Plex-Token", PlexToken);

        NowPlaying.Text = "Connecting to Plex";

        LyricsView.SeekToAsync = SeekToMsAsync;

        this.Closed += (_, __) =>
        {
            _pollCts?.Cancel();
            _plex?.Dispose();
            _artHttp.Dispose();
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
        // poll 5 times per second
        using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(200));
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
                _artist = _album = _title = _state = _artUrl = "";
                _durationMs = 0;
                _viewOffsetMs = 0;

                _predictedViewOffsetMs = 0;
                _predictedViewOffsetUtc = now;

                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateTrackInformation();
                    BackgroundGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x11, 0x11, 0x11));
                });
                return;
            }

            // Capture client id for control (seek)
            _clientId = np.ClientId ?? _clientId;

            // change detection
            bool trackChanged = np.Artist != _artist || np.Album != _album || np.Title != _title || np.DurationMs != _durationMs || np.ArtUrl != _artUrl;
            bool stateChanged = !np.State.Equals(_state, StringComparison.OrdinalIgnoreCase);
            bool viewOffsetChanged = np.ViewOffsetMs != _viewOffsetMs;

            if (trackChanged || stateChanged || viewOffsetChanged)
            {
                // track
                _artist = np.Artist;
                _album = np.Album;
                _title = np.Title;
                _durationMs = np.DurationMs;
                _state = np.State;
                _artUrl = np.ArtUrl;

                // viewOffset
                _viewOffsetMs = np.ViewOffsetMs;

                // prediction
                _predictedViewOffsetMs = _viewOffsetMs;
                _predictedViewOffsetUtc = now;
            }

            if (trackChanged)
            {
                var trackKey = $"{_artist}|{_album}|{_title}|{_durationMs}";
                await LyricsView.FetchLyricsAsync(_artist, _album, _title, trackKey, ct).ConfigureAwait(false);
                _ = UpdateBackgroundFromArtAsync(_artUrl, ct);
            }

            // Update labels
            DispatcherQueue.TryEnqueue(UpdateTrackInformation);
        }
        catch
        { }
    }

    private async Task UpdateBackgroundFromArtAsync(string artUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artUrl))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                BackgroundGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x11, 0x11, 0x11));
            });
            return;
        }

        try
        {
            var bytes = await _artHttp.GetByteArrayAsync(artUrl, ct).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            using var bmp = new Bitmap(ms);
            var colorThief = new ColorThief();
            var palette = colorThief.GetPalette(bmp, 5);
            if (palette is null || palette.Count == 0)
            {
                return;
            }

            var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            for (int i = 0; i < palette.Count; i++)
            {
                var c = palette[i].Color;
                double offset = palette.Count == 1 ? 0 : (double)i / (palette.Count - 1);
                brush.GradientStops.Add(new GradientStop
                {
                    Color = Windows.UI.Color.FromArgb(255, c.R, c.G, c.B),
                    Offset = offset
                });
            }

            DispatcherQueue.TryEnqueue(() => BackgroundGrid.Background = brush);
        }
        catch
        {
        }
    }

    internal void UpdateProgressFromPrediction()
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
    /// Updates artist/title text and delegates progress updates to the control panel.
    /// </summary>
    internal void UpdateTrackInformation()
    {
        ArtistBlock.Text = !string.IsNullOrWhiteSpace(_artist) ? _artist : "";
        NowPlaying.Text = !string.IsNullOrWhiteSpace(_title) ? _title : "Peace and quiet";
        ControlPanel.UpdateTrackInformation(_predictedViewOffsetMs, _durationMs, _state);
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
}
