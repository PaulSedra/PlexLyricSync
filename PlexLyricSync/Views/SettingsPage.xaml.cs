using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PlexLyricSync.Utils;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Views;

public sealed partial class SettingsPage
{
    private MainWindow? _mainWindow;
    private Config _config = new();
    private bool _loading;
    private readonly DispatcherTimer _debounceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SettingsPage()
    {
        InitializeComponent();
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _mainWindow = e.Parameter as MainWindow;
        _config = ConfigLoader.LoadExisting();

        _loading = true;
        NavTabs.SelectedIndex = 0;

        PlexUrlBox.Text = _config.PlexBaseUrl;
        PlexTokenBox.Text = _config.PlexToken;
        SyncedLinesBox.Value = _config.SyncedLyricLines;
        ShowJapaneseTransliterationSwitch.IsOn = _config.ShowJapaneseTransliteration;
        _loading = false;

    }

    private void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.PlexBaseUrl = PlexUrlBox.Text;
        _config.PlexToken = PlexTokenBox.Text;
        SaveConfigAndRestart();
    }

    private void ShowJapaneseTransliterationToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.ShowJapaneseTransliteration = ShowJapaneseTransliterationSwitch.IsOn;
        SaveConfigAndRestart();
    }

    private void SaveConfigAndRestart()
    {
        ConfigLoader.SaveConfig(_config);


        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SyncedLinesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || _mainWindow is null) return;

        _config.SyncedLyricLines = (int)sender.Value;
        SaveConfigAndRestart();
    }

    private void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();
        if (string.IsNullOrWhiteSpace(_config.PlexBaseUrl) || string.IsNullOrWhiteSpace(_config.PlexToken)) return;
        UpdateConfigAsync();
    }

    private void UpdateConfigAsync()
    {
        if (_mainWindow is null) return;

        _mainWindow.PollCts?.Cancel();
        _mainWindow.UiTimer.Stop();
        _ = _mainWindow.InitAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.CloseSettings();
    }

    private void NavTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PlexPanel.Visibility = Visibility.Collapsed;
        LyricsPanel.Visibility = Visibility.Collapsed;

        switch (NavTabs.SelectedIndex)
        {
            case 0:
                PlexPanel.Visibility = Visibility.Visible;
                break;
            case 1:
                LyricsPanel.Visibility = Visibility.Visible;
                break;
        }
    }

}
