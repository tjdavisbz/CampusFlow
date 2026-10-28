using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsAwardCodeMaintenance : IStudentInformationSystemAwardCodeMaintenance
{
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string ConnectionName = "ThesisElementsReadOnly";
    private const string TokenKey = "ThesisElements:AwardCodeMaintenance:Token";
    private static readonly HttpClient HttpClient = new();
    private static readonly string[] AwardFields =
    [
        "financialStatusId", "studentUid", "termCalendarId", "transDate", "transTypeId",
        "transStatusId", "amount", "estimatedAmount", "description", "referenceNo",
        "creditStatusId", "code1Id", "code2Id", "showOnBillingStatement", "dateCheckSigned",
        "requiredCredits", "studentAccepted", "transId", "lock", "campusId",
        "paymentPeriodStartDate", "paymentPeriodEndDate", "codaei", "originationFeeAmount",
        "interestRebateAmount", "calendarYear", "programAttendanceBeginDate",
        "workForcePellTuitionAndFees", "institutionalLimitApplied"
    ];
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public ThesisElementsAwardCodeMaintenance(IConfiguration configuration, IMemoryCache cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;
    public bool CanWrite =>
        _configuration.GetValue<bool>("AwardCodeMaintenance:Enabled") &&
        Uri.TryCreate(_configuration["AwardCodeMaintenance:ExpectedApiBaseUrl"], UriKind.Absolute, out var expected) &&
        Uri.TryCreate(_configuration[$"{ApiPath}:BaseUrl"], UriKind.Absolute, out var actual) &&
        expected == actual;

    public async Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP (50) LTRIM(RTRIM(TextTerm)) FROM dbo.TermCalendar " +
                           "WHERE TextTerm IS NOT NULL ORDER BY TermCalendarID DESC";
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        var terms = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var term = reader.GetString(0);
            if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase)) terms.Add(term);
        }
        return terms;
    }

    public async Task<IReadOnlyList<AwardCodeMismatch>> GetMismatchesAsync(string term, int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || take is < 1 or > 101)
            throw new ArgumentOutOfRangeException(nameof(take));
        const string sql = """
            SELECT TOP (@Take) f.FinancialAwardID, f.StudentUID, s.StudentID,
                   LTRIM(RTRIM(s.FirstName)) + ' ' + LTRIM(RTRIM(s.LastName)),
                   tc.TextTerm, f.[Description], f.Code1ID, fr.Code1ID,
                   f.ShowOnBillingStatement, fr.ShowOnBillStatement
            FROM dbo.FinancialAward f
            JOIN dbo.FinancialAwardRef fr ON fr.AwardTypeID = f.TransTypeID
            JOIN dbo.TermCalendar tc ON tc.TermCalendarID = f.TermCalendarID
            JOIN dbo.Student s ON s.StudentUID = f.StudentUID
            WHERE tc.TextTerm = @Term
              AND (ISNULL(f.Code1ID, -1) <> ISNULL(fr.Code1ID, -1)
                   OR ISNULL(f.ShowOnBillingStatement, 0) <> ISNULL(fr.ShowOnBillStatement, 0))
            ORDER BY f.FinancialAwardID
            """;
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        command.Parameters.Add("@Term", SqlDbType.VarChar, 50).Value = term.Trim();
        var rows = new List<AwardCodeMismatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new AwardCodeMismatch(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2).Trim(),
                reader.GetString(3).Trim(), reader.GetString(4).Trim(),
                reader.IsDBNull(5) ? "Award" : reader.GetString(5).Trim(),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                !reader.IsDBNull(8) && Convert.ToBoolean(reader.GetValue(8)),
                !reader.IsDBNull(9) && Convert.ToBoolean(reader.GetValue(9))));
        return rows;
    }

    public async Task UpdateAsync(AwardCodeMismatch expected, CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
            throw new InvalidOperationException("Award-code writes are disabled or the configured Elements API URL was not verified.");
        // Recheck the reference and award before each write; the read-only replica may lag the API.
        var currentRows = await GetMismatchesAsync(expected.Term, 101, cancellationToken);
        var current = currentRows.SingleOrDefault(x => x.AwardId == expected.AwardId);
        if (current is null || current != expected)
            throw new InvalidOperationException("The award or its reference values changed since the preview.");

        var token = await GetTokenAsync(cancellationToken);
        using var get = CreateRequest(HttpMethod.Get,
            $"api/financial-aid/maintenance/awards/financial-awards/{expected.StudentUid}", token);
        using var getResponse = await HttpClient.SendAsync(get, cancellationToken);
        getResponse.EnsureSuccessStatusCode();
        var awards = JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(cancellationToken))?
            ["data"]?["getFinancialAwardDetails"]?.AsArray()
            ?? throw new InvalidOperationException("Elements did not return the student's awards.");
        var award = awards.Select(x => x?.AsObject()).SingleOrDefault(x =>
            x?["financialAwardId"]?.GetValue<int>() == expected.AwardId &&
            x["studentUid"]?.GetValue<int>() == expected.StudentUid)
            ?? throw new InvalidOperationException("The selected award is no longer available in Elements.");
        if (ReadInt(award["code1Id"]) != expected.CurrentCodeId ||
            ReadBool(award["showOnBillingStatement"]) != expected.CurrentShowOnBill)
            throw new InvalidOperationException("Elements award values differ from the preview.");

        var body = new JsonObject();
        foreach (var field in AwardFields)
            if (award.TryGetPropertyValue(field, out var value)) body[field] = value?.DeepClone();
        body["code1Id"] = expected.ReferenceCodeId;
        body["showOnBillingStatement"] = expected.ReferenceShowOnBill;
        using var put = CreateRequest(HttpMethod.Put,
            $"api/financial-aid/maintenance/awards/financial-awards/{expected.AwardId}", token);
        put.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(put, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (JsonNode.Parse(content)?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements rejected the award update.");
    }

    private static int? ReadInt(JsonNode? node) => node is null ? null : node.GetValue<int>();

    private static bool ReadBool(JsonNode? node)
    {
        if (node is null) return false;
        var value = node.AsValue();
        return value.TryGetValue<bool>(out var flag) ? flag : value.GetValue<int>() != 0;
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;
        using var request = CreateRequest(HttpMethod.Get, "api/Login/Authenticate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Required("Username")}:{Required("Password")}")));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?
            ["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Elements returned no API token.");
        _cache.Set(TokenKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, $"{Required("BaseUrl").TrimEnd('/')}/{path.TrimStart('/')}");
        request.Headers.Add("TenantHost", Required("TenantHost"));
        request.Headers.Add("Module", Required("Module"));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private string Required(string name) => _configuration[$"{ApiPath}:{name}"] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.");

    private string GetConnectionString() => _configuration.GetConnectionString(ConnectionName) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Connection string '{ConnectionName}' is not configured.");
}
