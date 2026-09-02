namespace StickyNotes.Core.Models;

public sealed class AuthSession
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string IdToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
