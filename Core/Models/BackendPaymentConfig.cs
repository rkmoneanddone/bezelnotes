namespace StickyNotes.Core.Models;

public sealed class BackendPaymentConfig
{
    public bool Enabled { get; init; }
    public string IndiaProvider { get; init; } = "dodo";
    public string InternationalProvider { get; init; } = "dodo";
    public BackendPaymentPlanGroup SixMonth { get; init; } = new();
    public BackendPaymentPlanGroup Yearly { get; init; } = new();
}

public sealed class BackendPaymentPlanGroup
{
    public BackendPaymentPlan India { get; init; } = new();
    public BackendPaymentPlan International { get; init; } = new();
}

public sealed class BackendPaymentPlan
{
    public bool Enabled { get; init; }
    public int AmountMinor { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;

    // Safe public plan/product identifier only.
    // Gateway secrets must never be returned to the desktop.
    public string ProviderProductId { get; init; } = string.Empty;
}