using System.Net.Http;
using System.Text;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class GoogleBackendAuthService
{
    private readonly FirebaseClientConfig _config;
    private readonly HttpClient _httpClient = new();

    public GoogleBackendAuthService(
        FirebaseClientConfig config)
    {
        _config = config;
    }

    public async Task<AuthSession> ExchangeAsync(
        GoogleOAuthResult googleResult,
        CancellationToken cancellationToken = default)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException(
                "Firebase secure backend is not configured.");
        }

        string endpoint =
            _config.SecureApiBaseUrl.TrimEnd('/') +
            "/exchangeGoogleCode";

        var payload = new
        {
            code =
                googleResult.Code,

            codeVerifier =
                googleResult.CodeVerifier,

            redirectUri =
                googleResult.RedirectUri
        };

        using HttpResponseMessage response =
            await _httpClient.PostAsync(
                endpoint,
                new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"),
                cancellationToken);

        string body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        using JsonDocument document =
            JsonDocument.Parse(body);

        JsonElement root =
            document.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            string detail =
                root.TryGetProperty(
                    "detail",
                    out JsonElement detailElement)
                    ? detailElement.GetString()
                      ?? "Google authentication failed."
                    : "Google authentication failed.";

            throw new InvalidOperationException(
                detail);
        }

        string expiresInText =
            root.TryGetProperty(
                "expiresIn",
                out JsonElement expiresElement)
                ? expiresElement.GetString()
                  ?? "3600"
                : "3600";

        int expiresInSeconds =
            int.TryParse(
                expiresInText,
                out int parsed)
                ? parsed
                : 3600;

        return new AuthSession
        {
            UserId =
                root.GetProperty(
                    "userId").GetString()
                ?? string.Empty,

            Email =
                root.TryGetProperty(
                    "email",
                    out JsonElement emailElement)
                    ? emailElement.GetString()
                      ?? string.Empty
                    : string.Empty,

            IdToken =
                root.GetProperty(
                    "idToken").GetString()
                ?? string.Empty,

            RefreshToken =
                root.GetProperty(
                    "refreshToken").GetString()
                ?? string.Empty,

            ExpiresAtUtc =
                DateTimeOffset.UtcNow.AddSeconds(
                    Math.Max(
                        60,
                        expiresInSeconds - 60))
        };
    }
}