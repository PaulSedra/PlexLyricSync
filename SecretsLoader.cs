using System;
using System.IO;
using Windows.Storage;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlexLyricSync;

public static class SecretsLoader
{
    public sealed class Secrets
    {
        public string PlexBaseUrl { get; set; } = "";
        public string PlexToken { get; set; } = "";
    }

    public static Secrets LoadSecrets()
    {
        // Packaged-safe writable directory
        var localDir = ApplicationData.Current.LocalFolder.Path;
        var localPath = Path.Combine(localDir, "appsettings.secrets.yaml");

        // 1) If already in LocalFolder → use it
        if (File.Exists(localPath))
            return Deserialize(localPath);

        // Ensure LocalFolder exists
        Directory.CreateDirectory(localDir);

        // 3) Create a template in LocalFolder
        File.WriteAllText(localPath,
            "PlexBaseUrl: \"http://localhost:32400\"\n" +
            "PlexToken: \"PUT_TOKEN_HERE\"\n");

        return Deserialize(localPath);
    }

    private static Secrets Deserialize(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<Secrets>(yaml) ?? new Secrets();
    }
}