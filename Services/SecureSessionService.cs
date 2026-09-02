using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class SecureSessionService
{
    private readonly string _directory;
    private readonly string _path;

    public SecureSessionService()
    {
        _directory =
            System.IO.Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "StickyNotes");

        _path =
            System.IO.Path.Combine(
                _directory,
                "account-session.bin");
    }

    public bool HasSavedSession =>
        System.IO.File.Exists(_path);

    public void Save(
        AuthSession session)
    {
        System.IO.Directory.CreateDirectory(
            _directory);

        var persisted =
            new PersistedSession
            {
                UserId = session.UserId,
                Email = session.Email,
                RefreshToken = session.RefreshToken
            };

        byte[] plain =
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(
                    persisted));

        byte[] protectedBytes =
            ProtectedData.Protect(
                plain,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

        System.IO.File.WriteAllBytes(
            _path,
            protectedBytes);
    }

    public PersistedSession? Load()
    {
        if (!System.IO.File.Exists(_path))
        {
            return null;
        }

        try
        {
            byte[] protectedBytes =
                System.IO.File.ReadAllBytes(
                    _path);

            byte[] plain =
                ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<PersistedSession>(
                Encoding.UTF8.GetString(
                    plain));
        }
        catch
        {
            Clear();
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (System.IO.File.Exists(_path))
            {
                System.IO.File.Delete(_path);
            }
        }
        catch
        {
        }
    }
}

public sealed class PersistedSession
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}