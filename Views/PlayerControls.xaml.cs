using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using PlexLyricSync.Utils;

namespace PlexLyricSync.Views;

public sealed partial class PlayerControls
{
    public MainWindow? MainWindow { get; set; }
    public LyricsView? LyricsView { get; set; }

    private bool _isSeeking;  // true while user is dragging the progress bar
    internal double ControlsHeight;
    private double _controlsContentHeight;
    private const double ProgressHeight = 6;
    private const double ControlsHidePadding = 32;
    private const double ProgressSpacing = 14;
    private const double ProgressInsetPerSide = 16;

    public PlayerControls()
    {
        InitializeComponent();

        ControlsContainer.Loaded += (_, _) =>
        {
            _controlsContentHeight = ControlsContainer.ActualHeight;
            ControlsHeight = _controlsContentHeight + ControlsHidePadding;
            ControlsTranslate.Y = ControlsHeight;
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
        MainWindow? mainWindow = MainWindow;
        LyricsView? lyricView = LyricsView;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow.Plex is null || string.IsNullOrWhiteSpace(mainWindow.ClientUrl) || string.IsNullOrWhiteSpace(mainWindow.ClientId)) return;
            if (mainWindow.DurationMs <= 0) return;

            double x = pointerEvent.GetCurrentPoint(progressBar).Position.X;
            double fraction = progressBar.ActualWidth > 0 ? x / progressBar.ActualWidth : 0;
            fraction = Math.Clamp(fraction, 0, 1);
            int targetMs = (int)(mainWindow.DurationMs * fraction);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            if (!_isSeeking)
            {
                bool ok = await mainWindow.Plex.SeekToAsync(mainWindow.ClientUrl, mainWindow.ClientId, targetMs, cts.Token);
                if (!ok) return;
            }

            mainWindow.PredictedViewOffsetMs = targetMs;
            mainWindow.PredictedViewOffsetUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(lyricView!.Lyrics?.SyncedLrc) && lyricView.Lrc is not null && lyricView.Lrc.Count > 0)
                lyricView.CurrentLyricIndex = LrcParser.IndexAt(lyricView.Lrc, TimeSpan.FromMilliseconds(targetMs));
            else
                lyricView.CurrentLyricIndex = -1;

            mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                mainWindow.UpdateTrackProgress();
                if (!string.IsNullOrWhiteSpace(lyricView.Lyrics?.SyncedLrc) && lyricView.Lrc is not null && lyricView.Lrc.Count > 0)
                    lyricView.UpdateSyncedLyricStack(lyricView.CurrentLyricIndex);
            });
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Handles progress bar drag start.
    /// On drag start the app UI is updated but the plex client is not.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerPressed(object sender, PointerRoutedEventArgs pointerEvent)
    {
        try
        {
            MainWindow? mainWindow = MainWindow;
            if (mainWindow is null) return;
            _isSeeking = true;
            FrameworkElement progressBar = (FrameworkElement)sender;
            progressBar.CapturePointer(pointerEvent.Pointer);
            await SeekFromPointerAsync(progressBar, pointerEvent);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Handles dragging across the progress bar.
    /// On drag the app UI is updated but the plex client is not.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerMoved(object sender, PointerRoutedEventArgs pointerEvent)
    {
        try
        {
            MainWindow? mainWindow = MainWindow;
            if (mainWindow is null || !_isSeeking) return;
            FrameworkElement progressBar = (FrameworkElement)sender;
            await SeekFromPointerAsync(progressBar, pointerEvent);
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    /// Handles progress bar drag end.
    /// On drag end the plex client is updated.
    /// </summary>
    /// <param name="sender">the sender object</param>
    /// <param name="pointerEvent">the mouse pointer</param>
    private async void SongProgress_PointerReleased(object sender, PointerRoutedEventArgs pointerEvent)
    {
        try
        {
            MainWindow? mainWindow = MainWindow;
            if (mainWindow is null || !_isSeeking) return;
            _isSeeking = false;
            FrameworkElement progressBar = (FrameworkElement)sender;
            progressBar.ReleasePointerCaptures();
            await SeekFromPointerAsync(progressBar, pointerEvent);
        }
        catch
        {
            // ignored
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MainWindow?.OpenSettings();
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            MainWindow? mainWindow = MainWindow;
            if (mainWindow?.Plex is null || string.IsNullOrWhiteSpace(mainWindow.ClientUrl) || string.IsNullOrWhiteSpace(mainWindow.ClientId)) return;

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            bool ok;
            if (mainWindow.State.Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                mainWindow.ForecastTrackProgress();
                ok = await mainWindow.Plex.PauseAsync(mainWindow.ClientUrl, mainWindow.ClientId, cts.Token);
                if (!ok) return;
                mainWindow.State = "paused";
            }
            else
            {
                ok = await mainWindow.Plex.PlayAsync(mainWindow.ClientUrl, mainWindow.ClientId, cts.Token);
                if (!ok) return;
                mainWindow.State = "playing";
                mainWindow.PredictedViewOffsetUtc = DateTime.UtcNow;
            }
            
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.UpdateTrackProgress);
        }
        catch
        {
            // ignored
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SkipAsync(true);
            MainWindow?.Root.Focus(FocusState.Programmatic);
        }
        catch
        {
            // ignored
        }
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SkipAsync(false);
            MainWindow?.Root.Focus(FocusState.Programmatic);
        }
        catch
        {
            // ignored
        }
    }

    private async Task SkipAsync(bool forward)
    {
        MainWindow? mainWindow = MainWindow;
        if (mainWindow is null) return;
        try
        {
            if (mainWindow.Plex is null || string.IsNullOrWhiteSpace(mainWindow.ClientUrl) || string.IsNullOrWhiteSpace(mainWindow.ClientId)) return;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1.5));
            if (forward)
                await mainWindow.Plex.SkipNextAsync(mainWindow.ClientUrl, mainWindow.ClientId, cts.Token);
            else
                await mainWindow.Plex.SkipPreviousAsync(mainWindow.ClientUrl, mainWindow.ClientId, cts.Token);
        }
        catch
        {
            // ignored
        }
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
        TimeSpan ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Handles show/hide animation of the ControlsContainer.
    /// </summary>
    /// <param name="pixels">number of pixels to translate element</param>
    public void AnimateControls(double pixels)
    {
        Storyboard sb = new();
        bool showing = pixels == 0;
        CubicEase easing = new() { EasingMode = showing ? EasingMode.EaseOut : EasingMode.EaseIn };

        DoubleAnimation anim = new()
        {
            To = pixels,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, ControlsTranslate);
        Storyboard.SetTargetProperty(anim, "Y");

        DoubleAnimation songProgressTranslateAnimation = new()
        {
            To = showing ? - (_controlsContentHeight + ProgressSpacing + ProgressHeight) : 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(songProgressTranslateAnimation, SongProgressTranslate);
        Storyboard.SetTargetProperty(songProgressTranslateAnimation, "Y");

        double scaleTarget = showing ? ComputeShownScale() : 1.0;
        DoubleAnimation songProgressScaleAnimation = new()
        {
            To = scaleTarget,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = easing
        };
        Storyboard.SetTarget(songProgressScaleAnimation, SongProgressScale);
        Storyboard.SetTargetProperty(songProgressScaleAnimation, "ScaleX");

        sb.Children.Add(anim);
        sb.Children.Add(songProgressTranslateAnimation);
        sb.Children.Add(songProgressScaleAnimation);
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
