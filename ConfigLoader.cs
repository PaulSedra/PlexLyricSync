using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlexLyricSync;

public static class ConfigLoader
{
    public sealed class Config
    {
        public string PlexBaseUrl { get; set; } = "";
        public string PlexToken { get; set; } = "";
    }

    public static async Task<Config> LoadConfigAsync(Window window)
    {
        // Packaged-safe writable directory
        var localDir = ApplicationData.Current.LocalFolder.Path;
        var localPath = Path.Combine(localDir, "appsettings.config.yaml");

        // If config already exists, load it
        if (File.Exists(localPath))
            return Deserialize(localPath);

        // Ensure LocalFolder exists
        Directory.CreateDirectory(localDir);

        // Ask user for configuration
        var urlBox = new TextBox();
        var tokenBox = new TextBox();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Plex URL" });
        panel.Children.Add(urlBox);
        panel.Children.Add(new TextBlock { Text = "Plex Token" });
        panel.Children.Add(tokenBox);

        var dialog = new ContentDialog
        {
            Title = "Configure Plex",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = window.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return new Config();
        }

        var cfg = new Config
        {
            PlexBaseUrl = urlBox.Text,
            PlexToken = tokenBox.Text
        };

        File.WriteAllText(localPath,
            $"PlexBaseUrl: \"{cfg.PlexBaseUrl}\"\n" +
            $"PlexToken: \"{cfg.PlexToken}\"\n");

        return cfg;
    }

    private static Config Deserialize(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<Config>(yaml) ?? new Config();
    }
}