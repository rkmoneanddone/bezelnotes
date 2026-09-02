namespace StickyNotes.Core.Models;

public sealed class BackendAccountState
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string EntitlementState { get; init; } = "unknown";
    public DateTimeOffset? TrialStartedAtUtc { get; init; }
    public DateTimeOffset? TrialEndsAtUtc { get; init; }
    public int TrialDaysRemaining { get; init; }
    public bool PremiumEnabled { get; init; }
    public string PlanCode { get; init; } = "none";
}
