namespace StickyNotes.Core.Models;

public sealed class FirebaseClientConfig
{
    // Public Firebase client identifiers only.
    // These are NOT authorization secrets.
    public string ApiKey { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SecureApiBaseUrl { get; set; } = string.Empty;
    public string GoogleOAuthClientId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProjectId) &&
        !string.IsNullOrWhiteSpace(SecureApiBaseUrl);
}
