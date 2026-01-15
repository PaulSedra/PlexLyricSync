using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace PlexLyricSync;

public sealed partial class PlayerControls : UserControl
{
    public MainWindow? MainWindow { get; set; }
    public LyricsView? LyricsView { get; set; }

    private bool _isSeeking = false;     // true while user is dragging the progress bar
    internal double _controlsHeight;
    private double _controlsContentHeight;
    private double _progressHeight = 6;
    private const double ControlsHidePadding = 32;
    private const double ProgressSpacing = 14;
    private const double ProgressInsetPerSide = 16;

    public PlayerControls()
    {
        this.InitializeComponent();

        ControlsContainer.Loaded += (_, __) =>
        {
            _controlsContentHeight = ControlsContainer.ActualHeight;
            _controlsHeight = _controlsContentHeight + ControlsHidePadding;
            ControlsTranslate.Y = _controlsHeight;
            SongProgressScale.ScaleX = 1.0;
        };
    }

    /// <summary>
    /// Handles song progress bar drag behavior.
    /// </summary>
    /// <param name="progressBar">the progress bar element</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async Task SeekFromPointerAsync(FrameworkElement progressBar, PointerRoutedEventArgs pointerEvent)
    {
        var mainWindow = MainWindow;
        var lyricView = LyricsView;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientUrl) || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;
            if (mainWindow._durationMs <= 0) return;

            double x = pointerEvent.GetCurrentPoint(progressBar).Position.X;
            double fraction = progressBar.ActualWidth > 0 ? x / progressBar.ActualWidth : 0;
            fraction = Math.Clamp(fraction, 0, 1);
            int targetMs = (int)(mainWindow._durationMs * fraction);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (!_isSeeking)
            {
                var ok = await mainWindow._plex.SeekToAsync(mainWindow._clientUrl, mainWindow._clientId, targetMs, cts.Token);
                if (!ok) return;
            }

            mainWindow._predictedViewOffsetMs = targetMs;
            mainWindow._predictedViewOffsetUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(lyricView!._lyrics.syncedLrc) && lyricView._lrc is not null && lyricView._lrc.Count > 0)
                lyricView._curLyricIdx = LrcParser.IndexAt(lyricView._lrc, TimeSpan.FromMilliseconds(targetMs));
            else
                lyricView._curLyricIdx = -1;

            mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                mainWindow.UpdateTrackProgress();
                if (!string.IsNullOrWhiteSpace(lyricView!._lyrics.syncedLrc) && lyricView._lrc is not null && lyricView._lrc.Count > 0)
                    lyricView.UpdateSyncedLyricStack(lyricView._curLyricIdx);
            });
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
        var mainWindow = MainWindow;
        if (mainWindow is null) return;
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
        var mainWindow = MainWindow;
        if (mainWindow is null || !_isSeeking) return;
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
        var mainWindow = MainWindow;
        if (mainWindow is null || !_isSeeking) return;
        _isSeeking = false;
        var progressBar = (FrameworkElement)sender;
        progressBar.ReleasePointerCaptures();
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MainWindow?.OpenSettings();
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = MainWindow;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientUrl) || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            bool ok;
            if (mainWindow._state.Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                mainWindow.ForecastTrackProgress();
                ok = await mainWindow._plex.PauseAsync(mainWindow._clientUrl, mainWindow._clientId, cts.Token);
                if (!ok) return;
                mainWindow._state = "paused";
            }
            else
            {
                ok = await mainWindow._plex.PlayAsync(mainWindow._clientUrl, mainWindow._clientId, cts.Token);
                if (!ok) return;
                mainWindow._state = "playing";
                mainWindow._predictedViewOffsetUtc = DateTime.UtcNow;
            }
            
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.UpdateTrackProgress);
        }
        catch
        {}
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(true);
        MainWindow?.Root.Focus(FocusState.Programmatic);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(false);
        MainWindow?.Root.Focus(FocusState.Programmatic);
    }

    private async Task SkipAsync(bool forward)
    {
        var mainWindow = MainWindow;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientUrl) || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (forward)
                await mainWindow._plex.SkipNextAsync(mainWindow._clientUrl, mainWindow._clientId, cts.Token);
            else
                await mainWindow._plex.SkipPreviousAsync(mainWindow._clientUrl, mainWindow._clientId, cts.Token);
        }
        catch
        { }
    }

    public void UpdateTrackInformation(double predictedViewOffsetMs, double durationMs, string state)
    {
        SongProgressFill.Width = durationMs == 0
            ? 0
            : SongProgress.ActualWidth * Math.Clamp(predictedViewOffsetMs / durationMs, 0, 1);
        TimeLabel.Text = $"{FormatTime(predictedViewOffsetMs)} / {FormatTime(durationMs)}";
        PlayPauseIcon.Glyph = state.Equals("playing", StringComparison.OrdinalIgnoreCase) ? "\uf8ae" : "\uf5b0";
    }

    private static string FormatTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Handles show/hide animation of the ControlsContainer.
    /// </summary>
    /// <param name="pixels">number of pixels to translate element</param>
    public void AnimateControls(double pixels)
    {
        var sb = new Storyboard();
        bool showing = pixels == 0;
        var easing = new CubicEase { EasingMode = showing ? EasingMode.EaseOut : EasingMode.EaseIn };

        var anim = new DoubleAnimation
        {
            To = pixels,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, ControlsTranslate);
        Storyboard.SetTargetProperty(anim, "Y");

        var SongProgressTranslateAnimation = new DoubleAnimation
        {
            To = showing ? - (_controlsContentHeight + ProgressSpacing + _progressHeight) : 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(SongProgressTranslateAnimation, SongProgressTranslate);
        Storyboard.SetTargetProperty(SongProgressTranslateAnimation, "Y");

        double scaleTarget = showing ? ComputeShownScale() : 1.0;
        var SongProgressScaleAnimation = new DoubleAnimation
        {
            To = scaleTarget,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(SongProgressScaleAnimation, SongProgressScale);
        Storyboard.SetTargetProperty(SongProgressScaleAnimation, "ScaleX");

        sb.Children.Add(anim);
        sb.Children.Add(SongProgressTranslateAnimation);
        sb.Children.Add(SongProgressScaleAnimation);
        sb.Begin();
    }

    private double ComputeShownScale()
    {
        double width = SongProgress.ActualWidth;
        if (width <= 0) return 1.0;
        double targetWidth = Math.Max(width - (2 * ProgressInsetPerSide), 0);
        double scale = targetWidth / width;
        return Math.Clamp(scale, 0.1, 1.0);
    }
}
