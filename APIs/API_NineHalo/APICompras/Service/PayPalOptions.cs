namespace APINineHalo.Services;

public sealed class PayPalOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "Sandbox";
    public string Currency => "USD";
    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)
        && Environment is "Sandbox" or "Live";
    public string BaseUrl => Environment == "Live" ? "https://api-m.paypal.com/" : "https://api-m.sandbox.paypal.com/";
    public void RequireEnabled()
    {
        if (!Enabled) throw new APINineHalo.Entities.CartPurchaseException("PayPal todavía no está configurado. Selecciona otro método de pago.", 503, false);
    }
}
