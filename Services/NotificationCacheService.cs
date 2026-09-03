using System.IO;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class NotificationCacheService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly string _path;

    public NotificationCacheService()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StickyNotes");

        Directory.CreateDirectory(directory);

        _path =
            Path.Combine(
                directory,
                "notification-cache.json");
    }

    public NotificationCacheEnvelope? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<NotificationCacheEnvelope>(
                File.ReadAllText(_path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool IsRefreshDue(
        DateTimeOffset utcNow)
    {
        NotificationCacheEnvelope? cached =
            Load();

        if (cached is null)
        {
            return true;
        }

        return utcNow - cached.CheckedAtUtc >=
            TimeSpan.FromHours(6);
    }

    public void Save(
        BackendNotification? notification,
        DateTimeOffset checkedAtUtc)
    {
        var envelope =
            new NotificationCacheEnvelope
            {
                CheckedAtUtc = checkedAtUtc,
                Notification = notification
            };

        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(
                envelope,
                JsonOptions));
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
        }
    }
}

public sealed class NotificationCacheEnvelope
{
    public DateTimeOffset CheckedAtUtc { get; init; }
    public BackendNotification? Notification { get; init; }
}

public sealed class NotificationRefreshResponse
{
    public DateTimeOffset CheckedAtUtc { get; init; }
    public BackendNotification? Notification { get; init; }
}