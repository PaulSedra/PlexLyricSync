using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PlexLyricSync;

public sealed partial class SettingsPage : Page
{
    private MainWindow? _mainWindow;

    public SettingsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _mainWindow = e.Parameter as MainWindow;
        var cfg = ConfigLoader.LoadExisting();
        PlexUrlBox.Text = cfg.PlexBaseUrl;
        PlexTokenBox.Text = cfg.PlexToken;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is null) return;
        var cfg = new ConfigLoader.Config
        {
            PlexBaseUrl = PlexUrlBox.Text,
            PlexToken = PlexTokenBox.Text
        };
        ConfigLoader.SaveConfig(cfg);
        await _mainWindow.UpdateConfigAsync(cfg);
        _mainWindow.CloseSettings();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.CloseSettings();
    }
}
