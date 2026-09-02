using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using StickyNotes.Core.Models;

namespace StickyNotes.Services;

public sealed class SecureBackendService
{
    private readonly FirebaseClientConfig _config;
    private readonly HttpClient _httpClient = new();

    public SecureBackendService(FirebaseClientConfig config)
    {
        _config = config;
    }

    public async Task<BackendAccountState> BootstrapAccountAsync(
        AuthSession session)
    {
        string installationId = GetOrCreateInstallationId();

        var version = Assembly.GetExecutingAssembly().GetName().Version;

        string appVersion = version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        var request = new
        {
            installationId,
            platform = "Windows",
            osVersion = Environment.OSVersion.VersionString,
            appVersion
        };

        return await PostAuthenticatedAsync<BackendAccountState>(
            "bootstrapAccount",
            request,
            session.IdToken);
    }

    private async Task<T> PostAuthenticatedAsync<T>(
        string endpoint,
        object body,
        string idToken)
    {
        if (!_config.IsConfigured)
        {
            throw new InvalidOperationException("Firebase backend is not configured.");
        }

        string baseUrl = _config.SecureApiBaseUrl.TrimEnd('/');
        string url = $"{baseUrl}/{endpoint}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", idToken);

        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request);
        string responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Secure backend request failed ({(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<T>(
                   responseBody,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidOperationException(
                   "Secure backend returned an invalid response.");
    }

    private static string GetOrCreateInstallationId()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyNotes");

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "installation.id");

        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path).Trim();

            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        string installationId = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, installationId);

        return installationId;
    }
}
