using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace PlexLyricSync;

public sealed partial class PlayerControls : UserControl
{
    public MainWindow? HostWindow { get; set; }

    public PlayerControls()
    {
        this.InitializeComponent();
    }

    private async Task SeekFromPointerAsync(FrameworkElement progressBar, PointerRoutedEventArgs pointerEvent)
    {
        var host = HostWindow;
        if (host is null) return;
        try
        {
            if (host._plex is null || string.IsNullOrWhiteSpace(host._clientId)) return;
            if (host._durationMs <= 0) return;

            double x = pointerEvent.GetCurrentPoint(progressBar).Position.X;
            double fraction = progressBar.ActualWidth > 0 ? x / progressBar.ActualWidth : 0;
            fraction = Math.Clamp(fraction, 0, 1);
            int targetMs = (int)(host._durationMs * fraction);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (!host._isSeeking)
            {
                var ok = await host._plex.SeekToAsync(host._clientId, targetMs, cts.Token);
                if (!ok) return;
            }

            host._predictedViewOffsetMs = targetMs;
            host._predictedViewOffsetUtc = DateTime.UtcNow;

            if (host._hasSynced && host._lrc is not null && host._lrc.Count > 0)
                host._curLyricIdx = LrcParser.IndexAt(host._lrc, TimeSpan.FromMilliseconds(targetMs));
            else
                host._curLyricIdx = -1;

            host.DispatcherQueue.TryEnqueue(() =>
            {
                host.UpdateTrackInformation();
                if (host._hasSynced && host._lrc is not null && host._lrc.Count > 0)
                    host.UpdateSyncedLyricStack(host._curLyricIdx);
            });
        }
        catch
        { }
    }

    private async void SongProgress_PointerPressed(object sender, PointerRoutedEventArgs pointerEvent)
    {
        var host = HostWindow;
        if (host is null) return;
        host._isSeeking = true;
        var progressBar = (FrameworkElement)sender;
        progressBar.CapturePointer(pointerEvent.Pointer);
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    private async void SongProgress_PointerMoved(object sender, PointerRoutedEventArgs pointerEvent)
    {
        var host = HostWindow;
        if (host is null || !host._isSeeking) return;
        var progressBar = (FrameworkElement)sender;
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    private async void SongProgress_PointerReleased(object sender, PointerRoutedEventArgs pointerEvent)
    {
        var host = HostWindow;
        if (host is null || !host._isSeeking) return;
        host._isSeeking = false;
        var progressBar = (FrameworkElement)sender;
        progressBar.ReleasePointerCaptures();
        await SeekFromPointerAsync(progressBar, pointerEvent);
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        var host = HostWindow;
        if (host is null) return;
        try
        {
            if (host._plex is null || string.IsNullOrWhiteSpace(host._clientId)) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            bool ok;
            if (host._state.Equals("playing", StringComparison.OrdinalIgnoreCase))
            {
                host.UpdateProgressFromPrediction();
                ok = await host._plex.PauseAsync(host._clientId, cts.Token);
                if (!ok) return;
                host._state = "paused";
            }
            else
            {
                ok = await host._plex.PlayAsync(host._clientId, cts.Token);
                if (!ok) return;
                host._state = "playing";
                host._predictedViewOffsetUtc = DateTime.UtcNow;
            }

            host.DispatcherQueue.TryEnqueue(host.UpdateTrackInformation);
        }
        catch
        {}
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(true);
        HostWindow?.Root.Focus(FocusState.Programmatic);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await SkipAsync(false);
        HostWindow?.Root.Focus(FocusState.Programmatic);
    }

    private async Task SkipAsync(bool forward)
    {
        var host = HostWindow;
        if (host is null) return;
        try
        {
            if (host._plex is null || string.IsNullOrWhiteSpace(host._clientId)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            if (forward)
                await host._plex.SkipNextAsync(host._clientId, cts.Token);
            else
                await host._plex.SkipPreviousAsync(host._clientId, cts.Token);
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
}
