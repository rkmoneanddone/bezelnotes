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

    public DateTimeOffset? ServerRefreshedAtUtc { get; init; }
    public BackendPricing Pricing { get; init; } = new();
    public BackendAppConfig AppConfig { get; init; } = new();
    public BackendPaymentConfig PaymentConfig { get; init; } = new();
    public BackendNotification? Notification { get; init; }
}

public sealed class BackendPricing
{
    public int TrialDays { get; init; } = 7;
    public int MonthlyPriceCents { get; init; } = 149;
    public int YearlyPriceCents { get; init; } = 599;
    public string Currency { get; init; } = "USD";
    public int MonthlyPriceProtectionMonths { get; init; } = 12;
}

public sealed class BackendAppConfig
{
    public string LatestVersion { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = string.Empty;
    public string UpdateMessage { get; init; } = string.Empty;
    public string UpdateUrl { get; init; } = string.Empty;
    public bool ForceUpdate { get; init; }
    public bool PaymentsEnabled { get; init; }    public bool CloudSyncEnabled { get; init; }
    public int ClientRefreshHours { get; init; } = 48;
}

public sealed class BackendNotification
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Type { get; init; } = "info";
    public DateTimeOffset? StartAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}