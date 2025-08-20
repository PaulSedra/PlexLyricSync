using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PlexLyricSync;

public sealed partial class SettingsPage : Page
{
    private MainWindow? _mainWindow;
    private ConfigLoader.Config _cfg = new();

    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _mainWindow = e.Parameter as MainWindow;
        _cfg = ConfigLoader.LoadExisting();
        PlexUrlBox.Text = _cfg.PlexBaseUrl;
        PlexTokenBox.Text = _cfg.PlexToken;
    }

    private async void SettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_mainWindow is null) return;
        _cfg.PlexBaseUrl = PlexUrlBox.Text;
        _cfg.PlexToken = PlexTokenBox.Text;
        ConfigLoader.SaveConfig(_cfg);
        if (!string.IsNullOrWhiteSpace(_cfg.PlexBaseUrl) &&
            !string.IsNullOrWhiteSpace(_cfg.PlexToken))
        {
            await _mainWindow.UpdateConfigAsync(_cfg);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.CloseSettings();
    }
}
