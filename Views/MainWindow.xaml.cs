using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Microsoft.UI.Xaml.Media.Animation;
using PlexLyricSync.Clients;
using PlexLyricSync.Utils;
using PlexLyricSync.Models;

namespace PlexLyricSync.Views;

public sealed partial class MainWindow
{
    private const int StartWidth = 600, StartHeight = 600;

    private string _plexBaseUrl = "";
    private string _plexToken = "";

    internal PlexApiClient? Plex;
    internal string ClientUrl = "";      // Plex player's machineIdentifier
    internal string ClientId = "";       // Plex player's machineIdentifier
    internal CancellationTokenSource? PollCts;

    // latest plex metadata
    private string _ratingKey = "", _artist = "", _album = "", _title = "", _albumArtUrl = "";
    private bool _trackLiked;
    internal string State = "";
    internal int DurationMs;
    private int _viewOffsetMs;

    // prediction
    internal int PredictedViewOffsetMs;
    internal DateTime PredictedViewOffsetUtc = DateTime.UtcNow;

    // Display clock (predicted position between server ticks)
    internal readonly DispatcherTimer UiTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _controlsIdleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _pointerStillTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _controlsVisible;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(StartWidth, StartHeight));
        if (AppWindow.Presenter is OverlappedPresenter p)
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

        Closed += (_, _) =>
        {
            PollCts?.Cancel();
            UiTimer.Stop();
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
        try
        {
            // Ensure this only runs once
            ((FrameworkElement)sender).Loaded -= MainWindow_Loaded;

            NowPlaying.Text = "Connecting to Plex";

            await InitAsync();
        }
        catch
        {
            // ignored
        }
    }

    internal async Task InitAsync()
    {
        // plex url and token from config file
        Config config = await ConfigLoader.LoadConfigAsync(this);
        _plexBaseUrl = config.PlexBaseUrl;
        _plexToken = config.PlexToken;
        LyricsView.SetSyncedLineCount(config.SyncedLyricLines);
        LyricsView.SetTranslationEnabled(config.EnableTranslations);
        LyricsView.SetShowTranslations(config is { SyncedLyricLines: 0, ShowTranslations: true });
        LyricsView.SetPreferredLanguage(config.PreferredLanguage);

        Plex = new PlexApiClient(_plexBaseUrl, _plexToken);

        // Start UI prediction (keeps the bar moving smoothly between Plex updates)
        UiTimer.Tick += (_, _) => ForecastTrackProgress();
        UiTimer.Start();

        // Kick an immediate poll and the background poll loop
        PollCts = new CancellationTokenSource();
        await PollPlexAsync(PollCts.Token);
        _ = RunPollLoopAsync(PollCts.Token);
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        // poll 5 times per second
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(200));
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
            PlexampSession? session = await Plex!.GetPlexampSession(ct);

            if (session is null)
            {
                _ratingKey = _artist = _album = _title = _albumArtUrl = State = "";
                DurationMs = 0;
                _viewOffsetMs = 0;

                PredictedViewOffsetMs = 0;
                PredictedViewOffsetUtc = now;

                LyricsView.Lrc = null;
                LyricsView.SetNoLyrics("");

                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                return;
            }

            bool npTrackLiked = await Plex.GetTrackLikedAsync(session.RatingKey, ct);

            // Capture client id for control (seek)
            ClientUrl = $"http://{session.ClientUrl}:32500";
            ClientId = session.ClientId;

            // change detection
            bool trackChanged = session.RatingKey != _ratingKey || session.Artist != _artist || session.Album != _album || session.Title != _title || npTrackLiked != _trackLiked || session.DurationMs != DurationMs || session.AlbumArtUrl != _albumArtUrl;
            bool stateChanged = !session.State.Equals(State, StringComparison.OrdinalIgnoreCase);
            bool viewOffsetChanged = session.ViewOffsetMs != _viewOffsetMs;

            if (trackChanged || stateChanged || viewOffsetChanged)
            {
                // track
                _ratingKey = session.RatingKey;
                _artist = session.Artist;
                _album = session.Album;
                _title = session.Title;
                _trackLiked = npTrackLiked;
                _albumArtUrl = session.AlbumArtUrl;
                DurationMs = session.DurationMs;
                State = session.State;

                // viewOffset
                _viewOffsetMs = session.ViewOffsetMs;

                // prediction
                PredictedViewOffsetMs = _viewOffsetMs;
                PredictedViewOffsetUtc = now;
            }

            if (trackChanged)
            {
                string trackKey = $"{_artist}|{_album}|{_title}|{DurationMs}";
                DispatcherQueue.TryEnqueue(UpdateTrackInformation);
                await LyricsView.FetchLyricsAsync(_artist, _album, _title, DurationMs/1000, trackKey, ct).ConfigureAwait(false);
                await LyricsView.FetchTranslatedLyricsAsync(_artist, _album, _title, trackKey, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _artist = _album = _title = _albumArtUrl = State = "";
            DurationMs = 0;
            _viewOffsetMs = 0;

            PredictedViewOffsetMs = 0;
            PredictedViewOffsetUtc = DateTime.UtcNow;

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
        if (DurationMs <= 0)
        {
            PredictedViewOffsetMs = 0;
            UpdateTrackProgress();
            return;
        }

        DateTime now = DateTime.UtcNow;
        int elapsed = (int)Math.Ceiling((now - PredictedViewOffsetUtc).TotalMilliseconds);
        PredictedViewOffsetMs = State.Equals("playing", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(PredictedViewOffsetMs + Math.Max(0, elapsed), 0, DurationMs)
            : PredictedViewOffsetMs;
        PredictedViewOffsetUtc = now;

        UpdateTrackProgress();
    }

    internal async Task SeekToMsAsync(int targetMs)
    {
        try
        {
            if (Plex is null || string.IsNullOrWhiteSpace(ClientUrl) || string.IsNullOrWhiteSpace(ClientId)) return;

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            bool ok = await Plex.SeekToAsync(ClientUrl, ClientId, targetMs, cts.Token);
            if (!ok) return;

            PredictedViewOffsetMs = targetMs;
            PredictedViewOffsetUtc = DateTime.UtcNow;

            DispatcherQueue.TryEnqueue(UpdateTrackProgress);
        }
        catch
        {
            // ignored
        }
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
        ControlPanel.UpdateTrackInformation(PredictedViewOffsetMs, DurationMs, State);
        LyricsView.UpdateProgress(PredictedViewOffsetMs);
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
        ControlPanel.AnimateControls(ControlPanel.ControlsHeight);
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
        ControlPanel.AnimateControls(ControlPanel.ControlsHeight);
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
        try
        {
            if (Plex is null || string.IsNullOrWhiteSpace(_ratingKey)) return;

            _trackLiked = !_trackLiked;

            if (!_trackLiked)
            {
                Storyboard? sb = (Storyboard)FavRoot.Resources["UnfavoriteExplosion"];
                sb.Stop();
                sb.Begin();
            }

            UpdateTrackInformation();
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            await Plex.SetTrackLikedAsync(_ratingKey, _trackLiked, cts.Token);
        }
        catch
        {
            // ignored
        }
    }
}
