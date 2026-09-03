using System.IO;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class BackendBootstrapCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public BackendBootstrapCacheService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "StickyNotes");

        Directory.CreateDirectory(directory);
        _path = Path.Combine(
            directory,
            "backend-bootstrap-cache.json");
    }

    public BackendAccountState? LoadFresh(
        string userId,
        DateTimeOffset utcNow)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            CacheEnvelope? envelope =
                JsonSerializer.Deserialize<CacheEnvelope>(
                    File.ReadAllText(_path),
                    JsonOptions);

            if (envelope?.State is null ||
                !string.Equals(
                    envelope.State.UserId,
                    userId,
                    StringComparison.Ordinal))
            {
                return null;
            }

            int refreshHours =
                Math.Clamp(
                    envelope.State.AppConfig.ClientRefreshHours,
                    1,
                    168);

            if (utcNow - envelope.SavedAtUtc >=
                TimeSpan.FromHours(refreshHours))
            {
                return null;
            }

            return envelope.State;
        }
        catch
        {
            return null;
        }
    }

    public void Save(BackendAccountState state)
    {
        var envelope = new CacheEnvelope
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            State = state
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

    private sealed class CacheEnvelope
    {
        public DateTimeOffset SavedAtUtc { get; init; }
        public BackendAccountState State { get; init; } = new();
    }
}