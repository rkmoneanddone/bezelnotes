using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StickyNotes.Services;

public sealed class GoogleOAuthResult
{
    public string IdToken { get; init; } = string.Empty;
}

public sealed class GoogleOAuthService
{
    private readonly string _clientId;
    private readonly HttpClient _httpClient = new();

    public GoogleOAuthService(string clientId)
    {
        _clientId = clientId;
    }

    public async Task<GoogleOAuthResult> SignInAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException(
                "Google OAuth client ID is not configured.");
        }

        string state = CreateRandomUrlSafeValue(32);
        string codeVerifier = CreateRandomUrlSafeValue(64);
        string codeChallenge = CreateCodeChallenge(codeVerifier);

        using var listener =
            new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        int port =
            ((IPEndPoint)listener.LocalEndpoint).Port;

        string redirectUri =
            $"http://127.0.0.1:{port}/";

        string authorizationUrl =
            BuildAuthorizationUrl(
                redirectUri,
                state,
                codeChallenge);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = authorizationUrl,
                UseShellExecute = true
            });

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromMinutes(3));

        using TcpClient client =
            await listener.AcceptTcpClientAsync(
                timeoutCts.Token);

        using NetworkStream stream =
            client.GetStream();

        string requestText =
            await ReadHttpRequestAsync(
                stream,
                timeoutCts.Token);

        CallbackRequest callback =
            ParseCallbackRequest(
                requestText);

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            await WriteHttpResponseAsync(
                stream,
                BuildBrowserResponse(
                    "Google sign-in was cancelled."),
                timeoutCts.Token);

            throw new InvalidOperationException(
                $"Google sign-in failed: {callback.Error}");
        }

        if (!string.Equals(
                callback.State,
                state,
                StringComparison.Ordinal))
        {
            await WriteHttpResponseAsync(
                stream,
                BuildBrowserResponse(
                    "The sign-in request could not be verified."),
                timeoutCts.Token);

            throw new InvalidOperationException(
                "Google OAuth state validation failed.");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            await WriteHttpResponseAsync(
                stream,
                BuildBrowserResponse(
                    "Google sign-in did not return an authorization code."),
                timeoutCts.Token);

            throw new InvalidOperationException(
                "Google OAuth authorization code is missing.");
        }

        await WriteHttpResponseAsync(
            stream,
            BuildBrowserResponse(
                "Google sign-in completed. You can return to Bezel Sticky Notes."),
            timeoutCts.Token);

        string idToken =
            await ExchangeCodeAsync(
                callback.Code,
                redirectUri,
                codeVerifier,
                timeoutCts.Token);

        return new GoogleOAuthResult
        {
            IdToken = idToken
        };
    }

    private string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string codeChallenge)
    {
        var parameters =
            new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account"
            };

        string query =
            string.Join(
                "&",
                parameters.Select(
                    pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return
            $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
    }

    private async Task<string> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var content =
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                });

        using HttpResponseMessage response =
            await _httpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                content,
                cancellationToken);

        string body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string googleError =
                ParseGoogleTokenError(body);

            throw new InvalidOperationException(
                $"Google token exchange failed: {googleError}");
        }

        using JsonDocument document =
            JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty(
                "id_token",
                out JsonElement idTokenElement))
        {
            throw new InvalidOperationException(
                "Google did not return an ID token.");
        }

        return
            idTokenElement.GetString()
            ?? throw new InvalidOperationException(
                "Google ID token is empty.");
    }

    private static string ParseGoogleTokenError(
        string body)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(body);

            JsonElement root =
                document.RootElement;

            string error =
                root.TryGetProperty(
                    "error",
                    out JsonElement errorElement)
                    ? errorElement.GetString() ?? "unknown_error"
                    : "unknown_error";

            string description =
                root.TryGetProperty(
                    "error_description",
                    out JsonElement descriptionElement)
                    ? descriptionElement.GetString() ?? string.Empty
                    : string.Empty;

            return string.IsNullOrWhiteSpace(description)
                ? error
                : $"{error} - {description}";
        }
        catch
        {
            return "Google returned an unreadable token error.";
        }
    }
    private static async Task<string> ReadHttpRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        int read =
            await stream.ReadAsync(
                buffer,
                cancellationToken);

        if (read <= 0)
        {
            throw new InvalidOperationException(
                "Google OAuth callback was empty.");
        }

        return Encoding.ASCII.GetString(
            buffer,
            0,
            read);
    }

    private static CallbackRequest ParseCallbackRequest(
        string request)
    {
        string firstLine =
            request.Split(
                new[] { "\r\n" },
                StringSplitOptions.None)[0];

        string[] parts =
            firstLine.Split(' ');

        if (parts.Length < 2)
        {
            throw new InvalidOperationException(
                "Google OAuth callback was malformed.");
        }

        var uri =
            new Uri(
                "http://127.0.0.1" +
                parts[1]);

        Dictionary<string, string> values =
            ParseQueryString(
                uri.Query);

        return new CallbackRequest
        {
            Code =
                values.TryGetValue(
                    "code",
                    out string? code)
                    ? code
                    : null,

            State =
                values.TryGetValue(
                    "state",
                    out string? callbackState)
                    ? callbackState
                    : null,

            Error =
                values.TryGetValue(
                    "error",
                    out string? error)
                    ? error
                    : null
        };
    }

    private static Dictionary<string, string> ParseQueryString(
        string query)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        foreach (string part in
                 query.TrimStart('?').Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair =
                part.Split(
                    '=',
                    2);

            string key =
                Uri.UnescapeDataString(
                    pair[0].Replace("+", " "));

            string value =
                pair.Length > 1
                    ? Uri.UnescapeDataString(
                        pair[1].Replace("+", " "))
                    : string.Empty;

            result[key] = value;
        }

        return result;
    }

    private static async Task WriteHttpResponseAsync(
        NetworkStream stream,
        string html,
        CancellationToken cancellationToken)
    {
        byte[] body =
            Encoding.UTF8.GetBytes(
                html);

        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes =
            Encoding.ASCII.GetBytes(
                headers);

        await stream.WriteAsync(
            headerBytes,
            cancellationToken);

        await stream.WriteAsync(
            body,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static string BuildBrowserResponse(
        string message)
    {
        string safeMessage =
            WebUtility.HtmlEncode(
                message);

        return
            "<!doctype html>" +
            "<html><head>" +
            "<meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<title>Bezel Sticky Notes</title>" +
            "</head>" +
            "<body style=\"font-family:Segoe UI,Arial,sans-serif;padding:32px;color:#25282d\">" +
            "<h2>Bezel Sticky Notes</h2>" +
            $"<p>{safeMessage}</p>" +
            "<p>You may close this browser tab.</p>" +
            "</body></html>";
    }

    private static string CreateRandomUrlSafeValue(
        int byteCount)
    {
        byte[] bytes =
            RandomNumberGenerator.GetBytes(
                byteCount);

        return Base64UrlEncode(
            bytes);
    }

    private static string CreateCodeChallenge(
        string codeVerifier)
    {
        byte[] hash =
            SHA256.HashData(
                Encoding.ASCII.GetBytes(
                    codeVerifier));

        return Base64UrlEncode(
            hash);
    }

    private static string Base64UrlEncode(
        byte[] bytes)
    {
        return Convert.ToBase64String(
                bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class CallbackRequest
    {
        public string? Code { get; init; }
        public string? State { get; init; }
        public string? Error { get; init; }
    }
}