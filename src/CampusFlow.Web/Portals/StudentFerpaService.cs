using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Web.Portals;

public sealed record StudentFerpaEntry(int Id, int StudentUid, int FerpaItemId,
    bool AllowDisplay, string RelationCanRecv);

public sealed record StudentFerpaDisplay(int Id, string Label, bool IsIndividual, bool HasPassword);

public static class StudentFerpaFormatting
{
    private const string IndividualPrefix = "Individual Privacy Password:";
    private const string ContactPasswordMarker = " - Password:";

    public static StudentFerpaDisplay Describe(StudentFerpaEntry entry)
    {
        var value = entry.RelationCanRecv.Trim();
        if (value.StartsWith(IndividualPrefix, StringComparison.OrdinalIgnoreCase))
            return new(entry.Id, "Individual privacy password", true, true);

        var marker = value.IndexOf(ContactPasswordMarker, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            return new(entry.Id, value[..marker].Trim(), false, true);

        return new(entry.Id, "Other FERPA entry", false, false);
    }

    public static string Reveal(StudentFerpaEntry entry)
    {
        var value = entry.RelationCanRecv.Trim();
        if (value.StartsWith(IndividualPrefix, StringComparison.OrdinalIgnoreCase))
            return value[IndividualPrefix.Length..].Trim();

        var marker = value.IndexOf(ContactPasswordMarker, StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? value[(marker + ContactPasswordMarker.Length)..].Trim() : value;
    }

    public static string BuildIndividual(string password) =>
        IndividualPrefix + " " + ValidatePassword(password);

    public static string BuildContact(string relationship, string firstName,
        string lastName, string password)
    {
        var relation = ValidateLabel(relationship, nameof(relationship));
        var first = ValidateLabel(firstName, nameof(firstName));
        var last = ValidateLabel(lastName, nameof(lastName));
        var value = $"{relation}: {first}, {last}{ContactPasswordMarker} {ValidatePassword(password)}";
        if (value.Length > 250) throw new ArgumentException("The FERPA entry is too long.");
        return value;
    }

    private static string ValidatePassword(string password)
    {
        if (password?.Any(char.IsControl) == true)
            throw new ArgumentException("Enter a password of 1–50 printable characters.");
        var value = password?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 50)
            throw new ArgumentException("Enter a password of 1–50 printable characters.");
        return value;
    }

    private static string ValidateLabel(string label, string name)
    {
        if (label?.Any(char.IsControl) == true)
            throw new ArgumentException("Enter 1–50 characters without a colon or control character.", name);
        var value = label?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 50 || value.Contains(':') ||
            value.Contains(ContactPasswordMarker, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter 1–50 characters without a colon or control character.", name);
        return value;
    }
}

public interface IStudentFerpaService
{
    Task<IReadOnlyList<StudentFerpaEntry>> GetAsync(int studentUid, CancellationToken cancellationToken);
    Task AddIndividualAsync(int studentUid, string password, CancellationToken cancellationToken);
    Task AddContactAsync(int studentUid, string relationship, string firstName,
        string lastName, string password, CancellationToken cancellationToken);
    Task DeleteAsync(int studentUid, int entryId, CancellationToken cancellationToken);
}

/// <summary>Reads student privacy-password entries from the direct Elements Admissions API.</summary>
public sealed class StudentFerpaService(IConfiguration configuration, IHttpClientFactory clientFactory,
    IMemoryCache cache) : IStudentFerpaService, ITransientDependency
{
    private const int ConsentFormItemId = 5166;
    private const string ApiPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TokenCacheKey = "ThesisElements:StudentFerpa:IntegrationToken";

    public async Task<IReadOnlyList<StudentFerpaEntry>> GetAsync(int studentUid,
        CancellationToken cancellationToken)
    {
        if (studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("RegistrationBaseUrl").TrimEnd('/')}/api/student/student-ferpa/{studentUid}");
        AddTenantHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Elements could not load privacy records ({(int)response.StatusCode}).");

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("isSuccess", out var success) || success.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Elements returned an invalid privacy-record response.");

        var entries = new List<StudentFerpaEntry>();
        foreach (var row in data.EnumerateArray())
        {
            if (!row.TryGetProperty("studentUid", out var studentValue) ||
                !row.TryGetProperty("studentFerpaUid", out var idValue) ||
                !row.TryGetProperty("ferpaitemId", out var itemValue) ||
                !row.TryGetProperty("allowDisplay", out var displayValue) ||
                !row.TryGetProperty("relationCanRecv", out var textValue) ||
                !studentValue.TryGetInt32(out var owner) || !idValue.TryGetInt32(out var id) ||
                !itemValue.TryGetInt32(out var itemId) || textValue.ValueKind != JsonValueKind.String)
                continue;
            if (owner != studentUid || id <= 0 || itemId != ConsentFormItemId ||
                displayValue.ValueKind != JsonValueKind.True) continue;
            var text = textValue.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                entries.Add(new StudentFerpaEntry(id, owner, itemId, true, text));
        }
        return entries.OrderBy(x => x.Id).ToArray();
    }

    public async Task AddIndividualAsync(int studentUid, string password,
        CancellationToken cancellationToken)
    {
        var value = StudentFerpaFormatting.BuildIndividual(password);
        if ((await GetAsync(studentUid, cancellationToken)).Any(x =>
                StudentFerpaFormatting.Describe(x).IsIndividual))
            throw new InvalidOperationException("An individual privacy password already exists. Remove it before adding another.");
        await AddAsync(studentUid, value, cancellationToken);
    }

    public async Task AddContactAsync(int studentUid, string relationship, string firstName,
        string lastName, string password, CancellationToken cancellationToken)
    {
        var value = StudentFerpaFormatting.BuildContact(relationship, firstName, lastName, password);
        if ((await GetAsync(studentUid, cancellationToken)).Any(x =>
                string.Equals(x.RelationCanRecv.Trim(), value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That authorized-person entry already exists.");
        await AddAsync(studentUid, value, cancellationToken);
    }

    public async Task DeleteAsync(int studentUid, int entryId, CancellationToken cancellationToken)
    {
        if (entryId <= 0 || !(await GetAsync(studentUid, cancellationToken)).Any(x =>
                x.Id == entryId && StudentFerpaFormatting.Describe(x).HasPassword))
            throw new InvalidOperationException("That privacy-password entry is not available for this student.");
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"{Setting("RegistrationBaseUrl").TrimEnd('/')}/api/student/student-ferpa/{entryId}");
        AddTenantHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await SendChangeAsync(request, cancellationToken);
    }

    private async Task AddAsync(int studentUid, string relationCanRecv,
        CancellationToken cancellationToken)
    {
        if (studentUid <= 0) throw new ArgumentOutOfRangeException(nameof(studentUid));
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{Setting("RegistrationBaseUrl").TrimEnd('/')}/api/student/student-ferpa");
        AddTenantHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            allowDisplay = true,
            relationCanRecv,
            insertUserId = "CampusFlow",
            studentUid,
            studentFerpaUID = 0,
            ferpaitemId = ConsentFormItemId,
            userId = "CampusFlow"
        });
        await SendChangeAsync(request, cancellationToken);
    }

    private async Task SendChangeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await clientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Elements did not accept the privacy-record change ({(int)response.StatusCode}).");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("isSuccess", out var success) ||
            success.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Elements did not confirm the privacy-record change.");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{Setting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", Setting("TenantHost"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Setting("Username")}:{Setting("Password")}")));
        using var response = await clientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Elements authentication failed while loading privacy records.");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var token = document.RootElement.GetProperty("data").GetProperty("token").GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Elements authentication returned no token.");
        cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private void AddTenantHeaders(HttpRequestMessage request)
    {
        var tenantHost = Setting("TenantHost").TrimEnd('/');
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "Admissions");
        request.Headers.Add("TenantHost", tenantHost);
        request.Headers.Add("Origin", $"https://{tenantHost}");
        request.Headers.Referrer = new Uri($"https://{tenantHost}");
    }

    private string Setting(string name) => configuration[$"{ApiPath}:{name}"] is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Missing Elements API setting '{name}'.");
}
