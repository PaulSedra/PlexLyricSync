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
        InitializePreferredLanguageSelection();
        EnableTranslationsSwitch.IsOn = _config.EnableTranslations;
        UpdateShowTranslationsAvailability();
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
        UpdateShowTranslationsAvailability();

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void PreferredLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        if (PreferredLanguageBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag as string;
            if (string.Equals(tag, "system", StringComparison.OrdinalIgnoreCase))
            {
                _config.UseSystemLanguage = true;
                _config.PreferredLanguage = "";
            }
            else
            {
                _config.UseSystemLanguage = false;
                _config.PreferredLanguage = tag ?? "";
            }

            ConfigLoader.SaveConfig(_config);

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();
        if (string.IsNullOrWhiteSpace(_config.PlexBaseUrl) || string.IsNullOrWhiteSpace(_config.PlexToken)) return;
        UpdateConfigAsync(_config);
    }

    private void EnableTranslationsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;

        _config.EnableTranslations = EnableTranslationsSwitch.IsOn;
        ConfigLoader.SaveConfig(_config);

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ShowTranslationsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _mainWindow is null) return;
        if (_config.SyncedLyricLines != 0)
        {
            UpdateShowTranslationsAvailability();
            return;
        }

        _config.ShowTranslations = ShowTranslationsSwitch.IsOn;
        ConfigLoader.SaveConfig(_config);

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void UpdateShowTranslationsAvailability()
    {
        if (ShowTranslationsSwitch is null) return;
        if (ShowTranslationsContainer is not null)
        {
            ToolTipService.SetToolTip(
                ShowTranslationsContainer,
                _config.SyncedLyricLines == 0 ? null : "Set Synced lyric lines to 0 to enable translations.");
        }

        ShowTranslationsSwitch.IsEnabled = _config.SyncedLyricLines == 0;
        ShowTranslationsSwitch.IsOn = _config.ShowTranslations;
    }

    private Task UpdateConfigAsync(ConfigLoader.Config config)
    {
        if (_mainWindow is null)
            return Task.CompletedTask;

        _mainWindow._pollCts?.Cancel();
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
        LyricsPanel.Visibility = Visibility.Collapsed;

        if (NavTabs.SelectedIndex == 0)
        {
            PlexPanel.Visibility = Visibility.Visible;
        }
        else if (NavTabs.SelectedIndex == 1)
        {
            LyricsPanel.Visibility = Visibility.Visible;
        }
    }

    private void InitializePreferredLanguageSelection()
    {
        if (PreferredLanguageBox.Items.Count == 0) return;

        if (_config.UseSystemLanguage || string.IsNullOrWhiteSpace(_config.PreferredLanguage))
        {
            PreferredLanguageBox.SelectedIndex = 0; // System default
            return;
        }

        for (int i = 0; i < PreferredLanguageBox.Items.Count; i++)
        {
            if (PreferredLanguageBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, _config.PreferredLanguage, StringComparison.OrdinalIgnoreCase))
            {
                PreferredLanguageBox.SelectedIndex = i;
                return;
            }
        }

        PreferredLanguageBox.SelectedIndex = 0;
    }
}
