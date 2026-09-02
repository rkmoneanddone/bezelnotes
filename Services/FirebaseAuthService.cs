using System.Net.Http;
using System.Text;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class FirebaseAuthService
{
    private readonly FirebaseClientConfig _config;
    private readonly HttpClient _httpClient = new();

    public FirebaseAuthService(FirebaseClientConfig config)
    {
        _config = config;
    }

    public Task<AuthSession> SignInAsync(string email, string password)
        => AuthenticateAsync("accounts:signInWithPassword", email, password);

    public Task<AuthSession> CreateAccountAsync(string email, string password)
        => AuthenticateAsync("accounts:signUp", email, password);

    public async Task<AuthSession> SignInWithGoogleIdTokenAsync(
        string googleIdToken)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException(
                "Firebase is not configured.");
        }

        if (string.IsNullOrWhiteSpace(googleIdToken))
        {
            throw new InvalidOperationException(
                "Google ID token is missing.");
        }

        string url =
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={Uri.EscapeDataString(_config.ApiKey)}";

        var payload = new
        {
            postBody =
                $"id_token={Uri.EscapeDataString(googleIdToken)}&providerId=google.com",

            requestUri = "http://localhost",
            returnIdpCredential = true,
            returnSecureToken = true
        };

        using HttpResponseMessage response =
            await _httpClient.PostAsync(
                url,
                new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"));

        string body =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ParseFirebaseError(body));
        }

        using JsonDocument document =
            JsonDocument.Parse(body);

        JsonElement root =
            document.RootElement;

        int expiresInSeconds = 3600;

        if (root.TryGetProperty(
                "expiresIn",
                out JsonElement expiresElement))
        {
            int.TryParse(
                expiresElement.GetString(),
                out expiresInSeconds);
        }

        return new AuthSession
        {
            UserId =
                root.GetProperty(
                    "localId").GetString()
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
    private async Task<AuthSession> AuthenticateAsync(
        string operation,
        string email,
        string password)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException("Firebase is not configured.");
        }

        var payload = new
        {
            email,
            password,
            returnSecureToken = true
        };

        string url =
            $"https://identitytoolkit.googleapis.com/v1/{operation}?key={Uri.EscapeDataString(_config.ApiKey)}";

        using var response = await _httpClient.PostAsync(
            url,
            new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"));

        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ParseFirebaseError(body));
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        int expiresInSeconds = 3600;

        if (root.TryGetProperty("expiresIn", out var expiresInElement))
        {
            int.TryParse(expiresInElement.GetString(), out expiresInSeconds);
        }

        return new AuthSession
        {
            UserId = root.GetProperty("localId").GetString() ?? string.Empty,
            Email = root.GetProperty("email").GetString() ?? email,
            IdToken = root.GetProperty("idToken").GetString() ?? string.Empty,
            RefreshToken = root.GetProperty("refreshToken").GetString() ?? string.Empty,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(60, expiresInSeconds - 60))
        };
    }

    private static string ParseFirebaseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            string? message = document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();

            return string.IsNullOrWhiteSpace(message)
                ? "Firebase authentication failed."
                : message.Replace("_", " ");
        }
        catch
        {
            return "Firebase authentication failed.";
        }
    }
}
