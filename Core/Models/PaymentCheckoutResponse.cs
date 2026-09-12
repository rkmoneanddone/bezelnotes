namespace StickyNotes.Core.Models;

public sealed class PaymentCheckoutResponse
{
    public string CheckoutId { get; init; } = string.Empty;
    public string CheckoutUrl { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
}