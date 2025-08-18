using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace PlexLyricSync;

public sealed partial class PlayerControls : UserControl
{
    public MainWindow? MainWindow { get; set; }
    public LyricsView? LyricsView { get; set; }

    private bool _isSeeking = false;     // true while user is dragging the progress bar
    private double _controlsHeight;

    public PlayerControls()
    {
        this.InitializeComponent();

        ControlsContainer.Loaded += (_, __) =>
        {
            _controlsHeight = ControlsContainer.ActualHeight;
            ControlsTranslate.Y = _controlsHeight;
            ControlsContainer.Visibility = Visibility.Collapsed;
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
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;
            if (mainWindow._durationMs <= 0) return;

            double x = pointerEvent.GetCurrentPoint(progressBar).Position.X;
            double fraction = progressBar.ActualWidth > 0 ? x / progressBar.ActualWidth : 0;
            fraction = Math.Clamp(fraction, 0, 1);
            int targetMs = (int)(mainWindow._durationMs * fraction);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (!_isSeeking)
            {
                var ok = await mainWindow._plex.SeekToAsync(mainWindow._clientId, targetMs, cts.Token);
                if (!ok) return;
            }

            mainWindow._predictedViewOffsetMs = targetMs;
            mainWindow._predictedViewOffsetUtc = DateTime.UtcNow;

            if (lyricView!._hasSynced && lyricView._lrc is not null && lyricView._lrc.Count > 0)
                lyricView._curLyricIdx = LrcParser.IndexAt(lyricView._lrc, TimeSpan.FromMilliseconds(targetMs));
            else
                lyricView._curLyricIdx = -1;

            mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                mainWindow.UpdateTrackInformation();
                if (lyricView._hasSynced && lyricView._lrc is not null && lyricView._lrc.Count > 0)
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

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = MainWindow;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            bool ok;
            if (mainWindow._state.Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                mainWindow.UpdateProgressFromPrediction();
                ok = await mainWindow._plex.PauseAsync(mainWindow._clientId, cts.Token);
                if (!ok) return;
                mainWindow._state = "paused";
            }
            else
            {
                ok = await mainWindow._plex.PlayAsync(mainWindow._clientId, cts.Token);
                if (!ok) return;
                mainWindow._state = "playing";
                mainWindow._predictedViewOffsetUtc = DateTime.UtcNow;
            }

            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.UpdateTrackInformation);
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
            if (mainWindow._plex is null || string.IsNullOrWhiteSpace(mainWindow._clientId)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (forward)
                await mainWindow._plex.SkipNextAsync(mainWindow._clientId, cts.Token);
            else
                await mainWindow._plex.SkipPreviousAsync(mainWindow._clientId, cts.Token);
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
        PlayPauseIcon.Symbol = state.Equals("playing", StringComparison.OrdinalIgnoreCase) ? Symbol.Pause : Symbol.Play;
    }

    private static string FormatTime(double ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    public void ShowControls()
    {
        ControlsContainer.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, ControlsTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        sb.Children.Add(anim);
        sb.Begin();
    }

    public void HideControls()
    {
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            To = _controlsHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, ControlsTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        sb.Children.Add(anim);
        sb.Completed += (_, __) => ControlsContainer.Visibility = Visibility.Collapsed;
        sb.Begin();
    }
}
