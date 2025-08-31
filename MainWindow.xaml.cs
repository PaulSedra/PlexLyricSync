using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace PlexLyricSync;

public sealed partial class MainWindow : Window
{
    private const int startWidth = 600, startHeight = 600;

    internal string PlexBaseUrl = "";
    internal string PlexToken = "";

    internal PlexApiClient? _plex;
    internal string _clientId = "";       // Plex player's machineIdentifier
    private CancellationTokenSource? _pollCts;

    // latest plex metadata
    private string _artist = "", _album = "", _title = "", _albumArtUrl = "";
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
        LyricsView.MainWindow = this;

        ((FrameworkElement)Content).Loaded += MainWindow_Loaded;

        this.Closed += (_, __) =>
        {
            _pollCts?.Cancel();
            _plex?.Dispose();
            _uiTimer.Stop();
        };
    }

    internal void OpenSettings()
    {
        SettingsFrame.Navigate(typeof(SettingsPage), this);
        Root.Visibility = Visibility.Collapsed;
        SettingsFrame.Visibility = Visibility.Visible;
    }

    internal void CloseSettings()
    {
        SettingsFrame.Visibility = Visibility.Collapsed;
        Root.Visibility = Visibility.Visible;
    }

    internal async Task UpdateConfigAsync(ConfigLoader.Config config)
    {
        PlexBaseUrl = config.PlexBaseUrl;
        PlexToken = config.PlexToken;

        _pollCts?.Cancel();
        _plex?.Dispose();
        _pollCts = null;

        NowPlaying.Text = "Connecting to Plex";

        await InitAsync();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure this only runs once
        ((FrameworkElement)sender).Loaded -= MainWindow_Loaded;

        // plex url and token from config file
        var config = await ConfigLoader.LoadConfigAsync(this);
        PlexBaseUrl = config.PlexBaseUrl;
        PlexToken = config.PlexToken;

        NowPlaying.Text = "Connecting to Plex";

        await InitAsync();
    }

    private async Task InitAsync()
    {
        _plex = new PlexApiClient(PlexBaseUrl, PlexToken);

        // Start UI prediction (keeps the bar moving smoothly between Plex updates)
        _uiTimer.Tick += (_, __) => ForecastTrackProgress();
        _uiTimer.Start();

        // Kick an immediate poll and the background poll loop
        _pollCts = new CancellationTokenSource();
        await PollPlexAsync(_pollCts.Token);
        _ = RunPollLoopAsync(_pollCts.Token);
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        // poll 5 times per second
        using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await PollPlexAsync(ct);
        }
    }

    private async Task PollPlexAsync(CancellationToken ct)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            var np = await _plex!.GetPlexampNowPlayingAsync(ct);

            if (np is null)
            {
                _artist = _album = _title = _albumArtUrl = _state = "";
                _durationMs = 0;
                _viewOffsetMs = 0;

                _predictedViewOffsetMs = 0;
                _predictedViewOffsetUtc = now;

                LyricsView._lrc = null;

                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                return;
            }

            // Capture client id for control (seek)
            _clientId = np.ClientId ?? _clientId;

            // change detection
            bool trackChanged = np.Artist != _artist || np.Album != _album || np.Title != _title || np.DurationMs != _durationMs || np.AlbumArtUrl != _albumArtUrl;
            bool stateChanged = !np.State.Equals(_state, StringComparison.OrdinalIgnoreCase);
            bool viewOffsetChanged = np.ViewOffsetMs != _viewOffsetMs;

            if (trackChanged || stateChanged || viewOffsetChanged)
            {
                // track
                _artist = np.Artist;
                _album = np.Album;
                _title = np.Title;
                _albumArtUrl = np.AlbumArtUrl;
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
                var trackKey = $"{_artist}|{_album}|{_title}|{_durationMs}";
                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                await LyricsView.FetchLyricsAsync(_artist, _album, _title, trackKey, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            _artist = _album = _title = _albumArtUrl = _state = "";
            _durationMs = 0;
            _viewOffsetMs = 0;

            _predictedViewOffsetMs = 0;
            _predictedViewOffsetUtc = DateTime.UtcNow;

            LyricsView.SetNoLyrics("Unable to connect to Plex. Check server URL or token.");

            DispatcherQueue.TryEnqueue(() =>
            {
                ArtistBlock.Text = string.Empty;
                NowPlaying.Text = string.Empty;
                AlbumArtImage.Source = null;
            });
        }
    }

    internal void ForecastTrackProgress()
    {
        if (_durationMs <= 0)
        {
            _predictedViewOffsetMs = 0;
            UpdateTrackProgress();
            return;
        }

        DateTime now = DateTime.UtcNow;
        int elapsed = (int)Math.Ceiling((now - _predictedViewOffsetUtc).TotalMilliseconds);
        _predictedViewOffsetMs = _state.Equals("playing", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(_predictedViewOffsetMs + Math.Max(0, elapsed), 0, _durationMs)
            : _predictedViewOffsetMs;
        _predictedViewOffsetUtc = now;

        UpdateTrackProgress();
    }

    internal async Task SeekToMsAsync(int targetMs)
    {
        try
        {
            if (_plex is null || string.IsNullOrWhiteSpace(_clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var ok = await _plex.SeekToAsync(_clientId, targetMs, cts.Token);
            if (!ok) return;

            _predictedViewOffsetMs = targetMs;
            _predictedViewOffsetUtc = DateTime.UtcNow;

            DispatcherQueue.TryEnqueue(UpdateTrackProgress);
        }
        catch
        { }
    }

    /// <summary>
    /// Updates track title/artist and album art.
    /// </summary>
    internal void UpdateTrackInformation()
    {
        ArtistBlock.Text = !string.IsNullOrWhiteSpace(_artist) ? _artist : "";
        NowPlaying.Text = !string.IsNullOrWhiteSpace(_title) ? _title : "Peace and quiet";
        AlbumArtImage.Source = !string.IsNullOrWhiteSpace(_albumArtUrl) ? new BitmapImage(new Uri(_albumArtUrl)) : null;
    }

    /// <summary>
    /// Updates track progress by calling respective methods from LyricsView and ControlPanel.
    /// </summary>
    internal void UpdateTrackProgress()
    {
        ControlPanel.UpdateTrackInformation(_predictedViewOffsetMs, _durationMs, _state);
        LyricsView.UpdateProgress(_predictedViewOffsetMs);
    }

    /// <summary>
    /// Shows player controls when the mouse enters the window.
    /// </summary>
    private void Window_PointerEntered(object _, PointerRoutedEventArgs __)
    {
        ControlPanel.AnimateControls(0);
    }

    /// <summary>
    /// Hides player controls when the mouse leaves the window.
    /// </summary>
    private void Window_PointerExited(object _, PointerRoutedEventArgs __)
    {
        ControlPanel.AnimateControls(ControlPanel._controlsHeight);
    }
}
