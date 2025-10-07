using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PlexLyricSync;

public sealed partial class SettingsPage : Page
{
    private MainWindow? _mainWindow;
    private ConfigLoader.Config _config = new();
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
        _config = ConfigLoader.LoadExisting();

        _loading = true;
        PlexUrlBox.Text = _config.PlexBaseUrl;
        PlexTokenBox.Text = _config.PlexToken;
        SyncedLinesBox.Value = _config.SyncedLyricLines;
        _loading = false;

        NavTabs.SelectedIndex = 0;
    }

    private void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.PlexBaseUrl = PlexUrlBox.Text;
        _config.PlexToken = PlexTokenBox.Text;
        ConfigLoader.SaveConfig(_config);

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SyncedLinesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _mainWindow is null) return;

        _config.SyncedLyricLines = (int)sender.Value;
        ConfigLoader.SaveConfig(_config);

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();
        if (string.IsNullOrWhiteSpace(_config.PlexBaseUrl) || string.IsNullOrWhiteSpace(_config.PlexToken)) return;
        UpdateConfigAsync(_config);
    }

    private Task UpdateConfigAsync(ConfigLoader.Config config)
    {
        if (_mainWindow is null)
            return Task.CompletedTask;

        _mainWindow._pollCts?.Cancel();
        _mainWindow._plex?.Dispose();
        _mainWindow._uiTimer.Stop();
        return _mainWindow.InitAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.CloseSettings();
    }

    private void NavTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PlexPanel.Visibility = Visibility.Collapsed;

        // Only one panel currently, but structure allows future categories
        if (NavTabs.SelectedIndex == 0)
        {
            PlexPanel.Visibility = Visibility.Visible;
        }
    }
}
