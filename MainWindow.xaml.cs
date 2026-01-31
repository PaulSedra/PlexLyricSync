using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Microsoft.UI.Xaml.Media.Animation;

namespace PlexLyricSync;

public sealed partial class MainWindow : Window
{
    private const int startWidth = 600, startHeight = 600;

    internal string PlexBaseUrl = "";
    internal string PlexToken = "";

    internal PlexApiClient? _plex;
    internal string _clientUrl = "";      // Plex player's machineIdentifier
    internal string _clientId = "";       // Plex player's machineIdentifier
    internal CancellationTokenSource? _pollCts;

    // latest plex metadata
    private string _ratingKey = "", _artist = "", _album = "", _title = "", _albumArtUrl = "";
    private bool _trackLiked;
    internal string _state = "";
    internal int _durationMs = 0;
    private int _viewOffsetMs = 0;

    // prediction
    internal int _predictedViewOffsetMs = 0;
    internal DateTime _predictedViewOffsetUtc = DateTime.UtcNow;

    // Display clock (predicted position between server ticks)
    internal readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _controlsIdleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _pointerStillTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _controlsVisible;

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
        _controlsIdleTimer.Tick += ControlsIdleTimer_Tick;
        _pointerStillTimer.Tick += PointerStillTimer_Tick;

        this.Closed += (_, __) =>
        {
            _pollCts?.Cancel();
            _uiTimer.Stop();
            _controlsIdleTimer.Stop();
            _pointerStillTimer.Stop();
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

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure this only runs once
        ((FrameworkElement)sender).Loaded -= MainWindow_Loaded;

        NowPlaying.Text = "Connecting to Plex";

        await InitAsync();
    }

    internal async Task InitAsync()
    {
        // plex url and token from config file
        var config = await ConfigLoader.LoadConfigAsync(this);
        PlexBaseUrl = config.PlexBaseUrl;
        PlexToken = config.PlexToken;
        LyricsView.SetSyncedLineCount(config.SyncedLyricLines);
        LyricsView.SetTranslationEnabled(config.EnableTranslations);
        LyricsView.SetShowTranslations(config.SyncedLyricLines == 0 && config.ShowTranslations);
        LyricsView.SetPreferredLanguage(config.PreferredLanguage);

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
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
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
                _ratingKey = _artist = _album = _title = _albumArtUrl = _state = "";
                _durationMs = 0;
                _viewOffsetMs = 0;

                _predictedViewOffsetMs = 0;
                _predictedViewOffsetUtc = now;

                LyricsView._lrc = null;
                LyricsView.SetNoLyrics("");

                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                return;
            }

            var npTrackLiked = await _plex.GetTrackLikedAsync(np.RatingKey, ct);

            // Capture client id for control (seek)
            _clientUrl = "http://" + np.ClientUrl + ":32500" ?? _clientId;
            _clientId = np.ClientId ?? _clientId;

            // change detection
            bool trackChanged = np.RatingKey != _ratingKey || np.Artist != _artist || np.Album != _album || np.Title != _title || npTrackLiked != _trackLiked || np.DurationMs != _durationMs || np.AlbumArtUrl != _albumArtUrl;
            bool stateChanged = !np.State.Equals(_state, StringComparison.OrdinalIgnoreCase);
            bool viewOffsetChanged = np.ViewOffsetMs != _viewOffsetMs;

            if (trackChanged || stateChanged || viewOffsetChanged)
            {
                // track
                _ratingKey = np.RatingKey;
                _artist = np.Artist;
                _album = np.Album;
                _title = np.Title;
                _trackLiked = npTrackLiked;
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
                await LyricsView.FetchLyricsAsync(_artist, _album, _title, _durationMs/1000, trackKey, ct).ConfigureAwait(false);
                await LyricsView.FetchTranslatedLyricsAsync(_artist, _album, _title, trackKey, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _artist = _album = _title = _albumArtUrl = _state = "";
            _durationMs = 0;
            _viewOffsetMs = 0;

            _predictedViewOffsetMs = 0;
            _predictedViewOffsetUtc = DateTime.UtcNow;

            LyricsView.SetNoLyrics("Unable to connect to Plex. Check server URL or token." + ex);

            DispatcherQueue.TryEnqueue(() =>
            {
                FavoriteButton.Visibility = Visibility.Collapsed;
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
            if (_plex is null || string.IsNullOrWhiteSpace(_clientUrl) || string.IsNullOrWhiteSpace(_clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var ok = await _plex.SeekToAsync(_clientUrl, _clientId, targetMs, cts.Token);
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
    private void UpdateTrackInformation()
    {
        FavoriteIcon.Glyph = _trackLiked ? "\ueb52" : "\ueb51";
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
        HandlePointerActivity();
    }

    /// <summary>
    /// Hides player controls when the mouse leaves the window.
    /// </summary>
    private void Window_PointerExited(object _, PointerRoutedEventArgs __)
    {
        if (!_controlsVisible) return;
        ControlPanel.AnimateControls(ControlPanel._controlsHeight);
        _controlsVisible = false;
        _controlsIdleTimer.Stop();
        _pointerStillTimer.Stop();
    }

    private void Window_PointerMoved(object _, PointerRoutedEventArgs __)
    {
        HandlePointerActivity();
    }

    private void HandlePointerActivity()
    {
        if (_controlsVisible) return;
        ControlPanel.AnimateControls(0);
        _controlsVisible = true;
        _controlsIdleTimer.Stop();
        _pointerStillTimer.Stop();
        _pointerStillTimer.Start();
    }

    private void ControlsIdleTimer_Tick(object? sender, object e)
    {
        _controlsIdleTimer.Stop();
        if (!_controlsVisible) return;
        ControlPanel.AnimateControls(ControlPanel._controlsHeight);
        _controlsVisible = false;
    }

    private void PointerStillTimer_Tick(object? sender, object e)
    {
        _pointerStillTimer.Stop();
        if (!_controlsVisible) return;
        _controlsIdleTimer.Stop();
        _controlsIdleTimer.Start();
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plex is null || string.IsNullOrWhiteSpace(_ratingKey)) return;

        _trackLiked = !_trackLiked;

        if (!_trackLiked)
        {
            var sb = (Storyboard)FavRoot.Resources["UnfavoriteExplosion"];
            sb.Stop();
            sb.Begin();
        }

        UpdateTrackInformation();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        await _plex.SetTrackLikedAsync(_ratingKey, _trackLiked, cts.Token);
    }
}
