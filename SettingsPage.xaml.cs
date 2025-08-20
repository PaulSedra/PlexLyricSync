using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace PlexLyricSync;

public sealed partial class SettingsPage : Page
{
    private MainWindow? _mainWindow;
    private ConfigLoader.Config _cfg = new();
    private bool _loading = false;
    private readonly DispatcherTimer _debounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SettingsPage()
    {
        this.InitializeComponent();
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _mainWindow = e.Parameter as MainWindow;
        _cfg = ConfigLoader.LoadExisting();

        _loading = true;
        PlexUrlBox.Text = _cfg.PlexBaseUrl;
        PlexTokenBox.Text = _cfg.PlexToken;
        _loading = false;
    }

    private void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _cfg.PlexBaseUrl = PlexUrlBox.Text;
        _cfg.PlexToken = PlexTokenBox.Text;
        ConfigLoader.SaveConfig(_cfg);

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();
        if (_mainWindow is null) return;
        if (string.IsNullOrWhiteSpace(_cfg.PlexBaseUrl) || string.IsNullOrWhiteSpace(_cfg.PlexToken)) return;
        await _mainWindow.UpdateConfigAsync(_cfg);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.CloseSettings();
    }
}
