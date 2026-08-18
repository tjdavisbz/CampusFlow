namespace CampusFlow.Web.Payments;

public sealed class PayflowOptions
{
    public const string SectionName = "Payments:Payflow";
    public bool Enabled { get; set; }
    public bool TestMode { get; set; } = true;
    public string Partner { get; set; } = "PayPal";
    public string Vendor { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Vendor) && !string.IsNullOrWhiteSpace(Password);
    public string ApiUrl => TestMode ? "https://pilot-payflowpro.paypal.com" : "https://payflowpro.paypal.com";
}
