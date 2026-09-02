using System.IO;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class FirebaseClientConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string ConfigPath { get; }

    public FirebaseClientConfigService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNotes");

        Directory.CreateDirectory(directory);
        ConfigPath = Path.Combine(directory, "firebase-client.json");

        EnsureTemplateExists();
    }

    public FirebaseClientConfig Load()
    {
        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<FirebaseClientConfig>(json)
                   ?? new FirebaseClientConfig();
        }
        catch
        {
            return new FirebaseClientConfig();
        }
    }

    private void EnsureTemplateExists()
    {
        if (File.Exists(ConfigPath))
        {
            return;
        }

        var config = new FirebaseClientConfig();

        File.WriteAllText(
            ConfigPath,
            JsonSerializer.Serialize(config, JsonOptions));
    }
}
