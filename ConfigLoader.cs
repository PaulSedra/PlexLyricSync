using System;
using System.Globalization;
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
        public int SyncedLyricLines { get; set; } = 3;
        public string PreferredLanguage { get; set; } = "";
        public bool UseSystemLanguage { get; set; } = true;
        public bool EnableTranslations { get; set; } = false;
        public bool ShowTranslations { get; set; } = false;
    }

    private static string GetConfigPath()
    {
        var localDir = ApplicationData.Current.LocalFolder.Path;
        return Path.Combine(localDir, "appsettings.config.yaml");
    }

    public static Config LoadExisting()
    {
        var path = GetConfigPath();
        return File.Exists(path) ? Deserialize(path) : new Config();
    }

    public static void SaveConfig(Config cfg)
    {
        var path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $"PlexBaseUrl: \"{cfg.PlexBaseUrl}\"{Environment.NewLine}" +
            $"PlexToken: \"{cfg.PlexToken}\"{Environment.NewLine}" +
            $"SyncedLyricLines: {cfg.SyncedLyricLines}{Environment.NewLine}" +
            $"PreferredLanguage: \"{cfg.PreferredLanguage}\"{Environment.NewLine}" +
            $"UseSystemLanguage: {cfg.UseSystemLanguage}{Environment.NewLine}" +
            $"EnableTranslations: {cfg.EnableTranslations}{Environment.NewLine}" +
            $"ShowTranslations: {cfg.ShowTranslations}{Environment.NewLine}");
    }

    public static async Task<Config> LoadConfigAsync(Window window)
    {
        var path = GetConfigPath();

        // If config already exists, load it
        if (File.Exists(path))
            return Deserialize(path);

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

        var config = new Config
        {
            PlexBaseUrl = urlBox.Text,
            PlexToken = tokenBox.Text
        };

        SaveConfig(config);
        return config;
    }

    private static Config Deserialize(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        var cfg = deserializer.Deserialize<Config>(yaml) ?? new Config();

        // Backwards compatibility for configs written before new fields existed
        cfg.PreferredLanguage ??= "";

        if (!yaml.Contains("UseSystemLanguage:", StringComparison.OrdinalIgnoreCase))
        {
            cfg.UseSystemLanguage = true;
        }
        if (!yaml.Contains("EnableTranslations:", StringComparison.OrdinalIgnoreCase))
        {
            cfg.EnableTranslations = false;
        }
        if (!yaml.Contains("ShowTranslations:", StringComparison.OrdinalIgnoreCase))
        {
            cfg.ShowTranslations = false;
        }

        // Determine effective preferred language (config or system)
        if (cfg.UseSystemLanguage)
        {
            cfg.PreferredLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        return cfg;
    }
}
