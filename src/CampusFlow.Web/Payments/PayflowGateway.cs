using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace CampusFlow.Web.Payments;

public sealed record PayflowResponse(int Result, int? OriginalResult, string? Message, string? SecureToken, string? SecureTokenId,
    string? Reference, string? TransactionState)
{
    public bool IsApproved => Result == 0 && OriginalResult.GetValueOrDefault() == 0 && TransactionState == "8";
    public bool IsDirectSaleApproved => Result == 0;
}

public sealed record PayflowCard(string Number, int ExpirationMonth, int ExpirationYear, string SecurityCode,
    string? PostalCode);

public interface IPayflowGateway
{
    Task<PayflowResponse> SaleAsync(decimal amount, string invoiceNumber, PayflowCard card,
        CancellationToken cancellationToken);
}

public sealed class PayflowGateway(HttpClient client, IOptions<PayflowOptions> options) : IPayflowGateway
{
    private readonly PayflowOptions _options = options.Value;

    public Task<PayflowResponse> SaleAsync(decimal amount, string invoiceNumber, PayflowCard card,
        CancellationToken cancellationToken)
    {
        var expirationYear = card.ExpirationYear % 100;
        var values = new Dictionary<string, string>
        {
            ["TRXTYPE"] = "S",
            ["TENDER"] = "C",
            ["AMT"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["CURRENCY"] = _options.Currency,
            ["INVNUM"] = invoiceNumber,
            ["ACCT"] = card.Number,
            ["EXPDATE"] = $"{card.ExpirationMonth:00}{expirationYear:00}",
            ["CVV2"] = card.SecurityCode
        };
        if (!string.IsNullOrWhiteSpace(card.PostalCode)) values["ZIP"] = card.PostalCode.Trim();
        return SendAsync(values, cancellationToken);
    }

    private async Task<PayflowResponse> SendAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        values["PARTNER"] = _options.Partner; values["VENDOR"] = _options.Vendor;
        values["USER"] = string.IsNullOrWhiteSpace(_options.User) ? _options.Vendor : _options.User;
        values["PWD"] = _options.Password;
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl)
        {
            Content = new StringContent(Serialize(values), Encoding.UTF8, "text/namevalue")
        };
        request.Headers.TryAddWithoutValidation("X-VPS-REQUEST-ID", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("X-VPS-CLIENT-TIMEOUT", "30");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var parsed = Parse(body);
        _ = int.TryParse(Get(parsed, "RESULT"), out var result);
        int? originalResult = int.TryParse(Get(parsed, "ORIGRESULT"), out var original) ? original : null;
        return new(result, originalResult, Get(parsed, "RESPMSG"), Get(parsed, "SECURETOKEN"), Get(parsed, "SECURETOKENID"),
            Get(parsed, "PNREF"), Get(parsed, "TRANSSTATE"));
    }

    public static Dictionary<string, string> Parse(string body) => body.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(
            pair => Uri.UnescapeDataString(pair[0]).Split('[', 2)[0],
            pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
            StringComparer.OrdinalIgnoreCase);
    internal static string Serialize(IReadOnlyDictionary<string, string> values) => string.Join("&",
        values.Select(pair => $"{pair.Key}[{Encoding.UTF8.GetByteCount(pair.Value)}]={pair.Value}"));
    private static string? Get(Dictionary<string, string> values, string key) => values.GetValueOrDefault(key);
}
