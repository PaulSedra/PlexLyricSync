using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PlexLyricSync.Core.Models;

namespace PlexLyricSync.Utils;

public static class ConfigLoader
{

    private static string GetConfigPath()
    {
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Path.Combine(music, "PlexLyricSync", "appsettings.config.yaml");
    }

    public static Config LoadExisting()
    {
        string path = GetConfigPath();
        return File.Exists(path) ? Deserialize(path) : new Config();
    }

    public static void SaveConfig(Config cfg)
    {
        string path = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            $"PlexBaseUrl: \"{cfg.PlexBaseUrl}\"{Environment.NewLine}" +
            $"PlexToken: \"{cfg.PlexToken}\"{Environment.NewLine}" +
            $"SyncedLyricLines: {cfg.SyncedLyricLines}{Environment.NewLine}" +
            $"LibreTranslateBaseUrl: {cfg.LibreTranslateBaseUrl}{Environment.NewLine}" +
            $"PreferredLanguage: \"{cfg.PreferredLanguage}\"{Environment.NewLine}" +
            $"UseSystemLanguage: {cfg.UseSystemLanguage}{Environment.NewLine}" +
            $"EnableTranslations: {cfg.EnableTranslations}{Environment.NewLine}" +
            $"ShowTranslations: {cfg.ShowTranslations}{Environment.NewLine}");
    }

    public static Config LoadConfigAsync()
    {
        string path = GetConfigPath();
        return Deserialize(path);
    }

    public static async Task<Config> LoadConfigAsync(Window window)
    {
        string path = GetConfigPath();

        // If config already exists, load it
        if (File.Exists(path))
            return Deserialize(path);

        // Ask user for configuration
        TextBox urlBox = new();
        TextBox tokenBox = new();

        StackPanel panel = new();
        panel.Children.Add(new TextBlock { Text = "Plex URL" });
        panel.Children.Add(urlBox);
        panel.Children.Add(new TextBlock { Text = "Plex Token" });
        panel.Children.Add(tokenBox);

        ContentDialog dialog = new()
        {
            Title = "Configure Plex",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = window.Content.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return new Config();
        }

        Config config = new Config
        {
            PlexBaseUrl = urlBox.Text,
            PlexToken = tokenBox.Text
        };

        SaveConfig(config);
        return config;
    }

    private static Config Deserialize(string path)
    {
        string yaml = File.ReadAllText(path);
        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        Config cfg = deserializer.Deserialize<Config>(yaml);

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
