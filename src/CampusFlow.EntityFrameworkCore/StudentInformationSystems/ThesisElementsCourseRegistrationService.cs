using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsCourseRegistrationService : IStudentInformationSystemCourseRegistrationService
{
    private const string ConfigurationPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string TokenCacheKey = "ThesisElements:Registration:ApiToken";
    private const string RegistrationMode = "Unofficial";
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly IStudentInformationSystemCourseSelectionLookup _lookup;

    public ThesisElementsCourseRegistrationService(
        IConfiguration configuration,
        IMemoryCache memoryCache,
        IStudentInformationSystemCourseSelectionLookup lookup)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
        _lookup = lookup;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<string> AddUnofficialCourseAsync(
        string externalStudentId, string externalTermId, string externalOfferingId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var studentUid = ParseId(externalStudentId, nameof(externalStudentId));
        var termId = ParseId(externalTermId, nameof(externalTermId));
        var offeringId = ParseId(externalOfferingId, nameof(externalOfferingId));

        string token;
        RegistrationState state;
        try
        {
            token = await GetAccessTokenAsync(cancellationToken);
            state = await LoadFreshStateAsync(studentUid, termId, offeringId, token, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new CourseRegistrationException(
                "Elements registration could not be started.", false, exception);
        }

        var existing = FindRegistration(state.RegisteredCourses, offeringId);
        if (existing is not null && ReadInt(existing["srAcademicID"]) is > 0 and var existingId)
        {
            await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
            return existingId.ToString();
        }

        var newCourse = BuildNewRegistration(state.Offering, state.RegisteredCourses, studentUid, termId);
        state.RegisteredCourses.Add(newCourse);
        var totalCredits = state.RegisteredCourses.Sum(x => ReadDecimal(x?["credits"]));
        var hasNonWithdrawnCourseAttempt = await _lookup.HasNonWithdrawnCourseAttemptAsync(
            externalStudentId,
            ReadString(state.Offering["department"]),
            ReadString(state.Offering["courseID"]),
            ReadString(state.Offering["courseType"]),
            cancellationToken);

        try
        {
            await ValidateRegistrationAsync(studentUid, termId, state.Offering,
                state.RegisteredCourses, hasNonWithdrawnCourseAttempt, token, cancellationToken);
        }
        catch (CourseRegistrationValidationException)
        {
            await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
            throw new CourseRegistrationException(
                "Elements could not validate the registration request.", false, exception);
        }

        try
        {
            await SaveRegistrationAsync(studentUid, termId, state.RegisteredCourses,
                totalCredits, token, cancellationToken);
        }
        catch
        {
            await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
            throw;
        }

        var verified = await WaitForRegistrationAsync(
            externalStudentId, termId.ToString(), externalOfferingId, cancellationToken);
        return verified?.ExternalRegistrationId
               ?? throw new CourseRegistrationException(
                   "Elements accepted the registration request, but the unofficial course could not be verified.",
                   true, new InvalidOperationException("The new registration was not visible in the read-only database."));
    }

    public async Task RemoveCourseAsync(
        string externalStudentId, string externalTermId, string externalOfferingId,
        string externalRegistrationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        var studentUid = ParseId(externalStudentId, nameof(externalStudentId));
        var termId = ParseId(externalTermId, nameof(externalTermId));
        var offeringId = ParseId(externalOfferingId, nameof(externalOfferingId));
        var registrationId = ParseId(externalRegistrationId, nameof(externalRegistrationId));

        var before = await _lookup.GetRegistrationsAsync(externalStudentId, externalTermId, cancellationToken);
        if (before.All(x => x.ExternalRegistrationId != externalRegistrationId)) return;

        var token = await GetAccessTokenAsync(cancellationToken);
        await SetSemaphoreAsync(studentUid, termId, token, cancellationToken);
        try
        {
            var loadData = await LoadRegistrationInfoAsync(studentUid, termId, token, cancellationToken);
            var courses = loadData["registeredCourses"]?.AsArray()
                          ?? throw new InvalidOperationException("Elements did not return the registered courses.");
            var match = courses.SingleOrDefault(x =>
                ReadInt(x?["srAcademicID"]) == registrationId && ReadInt(x?["srOfferID"]) == offeringId);
            if (match is null)
            {
                await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
                return;
            }
            var course = match.AsObject();
            var currentStatus = ReadString(course["courseStatus"]);
            course["courseStatus"] = string.Equals(currentStatus, "PREV-AUDIT",
                StringComparison.OrdinalIgnoreCase) ? "PREV-DROP-AUDIT" : "PREV-DROP";
            var totalCredits = courses
                .Where(x => !ReadString(x?["courseStatus"]).Contains("DROP", StringComparison.OrdinalIgnoreCase))
                .Sum(x => ReadDecimal(x?["credits"]));
            await SaveRegistrationAsync(studentUid, termId, courses,
                totalCredits, token, cancellationToken);
        }
        catch
        {
            await CancelRegistrationSessionAsync(studentUid, termId, token, cancellationToken);
            throw;
        }

        if (!await WaitForRegistrationRemovalAsync(
                externalStudentId, externalTermId, externalRegistrationId, cancellationToken))
        {
            throw new InvalidOperationException(
                "Elements accepted the drop request, but the course removal could not be verified.");
        }
    }

    private async Task<RegistrationState> LoadFreshStateAsync(
        int studentUid, int termId, int offeringId, string token, CancellationToken cancellationToken)
    {
        // The read-only lookup proves an existing Student Status before load-info is called;
        // load-info is documented to create one when absent and is therefore part of this write transaction.
        var context = await _lookup.GetContextAsync(studentUid.ToString(), termId.ToString(), cancellationToken)
                      ?? throw new InvalidOperationException("The student does not have an Elements status for this term.");
        await SetSemaphoreAsync(studentUid, termId, token, cancellationToken);

        var loadData = await LoadRegistrationInfoAsync(studentUid, termId, token, cancellationToken);
        var offering = loadData["regOffers"]?.AsArray()
            .SingleOrDefault(x => ReadInt(x?["srOfferID"]) == offeringId)?.AsObject()
            ?? throw new InvalidOperationException("The selected offering was not returned by Elements registration.");
        var registered = loadData["registeredCourses"]?.AsArray()
                         ?? throw new InvalidOperationException("Elements did not return the registered courses.");
        return new RegistrationState(termId, offering, registered);
    }

    private async Task<JsonObject> LoadRegistrationInfoAsync(
        int studentUid, int termId, string token, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["stuId"] = studentUid,
            ["termId"] = termId,
            ["campuListIds"] = null,
            ["registrationMode"] = RegistrationMode
        };
        return await SendForDataAsync(HttpMethod.Post, "api/academic/register/load-info",
            body, token, cancellationToken);
    }

    private async Task SetSemaphoreAsync(
        int studentUid, int termId, string token, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["studentUID"] = studentUid, ["termID"] = termId };
        await SendForDataAsync(HttpMethod.Post, "api/academic/register/set-semaphore",
            body, token, cancellationToken);
    }

    private async Task SaveRegistrationAsync(
        int studentUid, int termId, JsonArray courses, decimal totalCredits,
        string token, CancellationToken cancellationToken)
    {
        // Elements initializes and returns the current user's registration defaults through this endpoint.
        // Calling it before save is required by the Registration module even though save accepts the values
        // in its request body.
        await SendForDataAsync(HttpMethod.Get, "api/academic/register/parameters-select-items",
            null, token, cancellationToken);
        var body = new JsonObject
        {
            ["studentUID"] = studentUid,
            ["registrationMode"] = RegistrationMode,
            ["userRegParameters"] = new JsonObject
            {
                ["termCalendarID"] = termId,
                ["regEffectiveDT"] = DateTime.UtcNow,
                ["checkConflicts"] = true,
                ["checkPreReqs"] = true,
                ["registerSaveOption"] = "Portal",
                ["checkCoReqs"] = true,
                ["checkPreReqInEquiv"] = true,
                ["autoLoadCoReqs"] = false,
                ["notifyRepeats"] = true,
                ["checkFinPackageLoad"] = true,
                ["accessCampusOptions"] = null,
                ["adminAddDropOverride"] = false,
                ["userid"] = "CampusFlow"
            },
            ["registredCourses"] = courses.DeepClone(),
            ["totalCredits"] = totalCredits,
            ["waitingList"] = new JsonArray()
        };
        await SendForDataNodeAsync(HttpMethod.Post, "api/academic/register/save",
            body, token, cancellationToken);
    }

    private async Task ValidateRegistrationAsync(
        int studentUid, int termId, JsonObject offering, JsonArray proposedCourses,
        bool hasNonWithdrawnCourseAttempt,
        string token, CancellationToken cancellationToken)
    {
        var offeringId = ReadInt(offering["srOfferID"]);
        var masterCourseId = ReadInt(offering["srMasterID"]);

        var prerequisite = await SendForDataNodeAsync(HttpMethod.Post,
            "api/academic/register/check-prerequisites-validation", new JsonObject
            {
                ["srMasterID"] = masterCourseId,
                ["studentUID"] = studentUid,
                ["checkEquivalents"] = true,
                ["returnvalue"] = false,
                ["termcalendarID"] = termId
            }, token, cancellationToken);
        var prerequisiteMessage = ReadString(prerequisite);
        if (!string.IsNullOrWhiteSpace(prerequisiteMessage))
            throw new CourseRegistrationValidationException(
                $"You do not meet the prerequisite requirements for this course. {prerequisiteMessage.Trim()}");

        var corequisite = (await SendForDataNodeAsync(HttpMethod.Post,
            "api/academic/register/check-corequisites-validation", new JsonObject
            {
                ["registeredCourses"] = proposedCourses.DeepClone(),
                ["srofferID"] = offeringId,
                ["termcalendarID"] = termId,
                ["studentUID"] = studentUid
            }, token, cancellationToken))?.AsObject();
        if (corequisite?["coRequisitePass"]?.GetValue<bool>() == false)
        {
            var formula = ReadString(corequisite["formulaText"]);
            throw new CourseRegistrationValidationException(string.IsNullOrWhiteSpace(formula)
                ? "This course requires a corequisite course that is not currently on your schedule."
                : $"This course requires the following corequisite: {formula.Trim()}");
        }

        var otherOfferingIds = string.Join(",", proposedCourses
            .Select(x => ReadInt(x?["srOfferID"]))
            .Where(x => x > 0 && x != offeringId));
        // Elements returns HTTP 400 when asked to compare against an empty course list.
        // With no other courses on the schedule, a schedule conflict is impossible.
        if (!string.IsNullOrWhiteSpace(otherOfferingIds))
        {
            var conflict = (await SendForDataNodeAsync(HttpMethod.Post,
                "api/academic/register/check-schedule-conflicts-validation", new JsonObject
                {
                    ["studentUID"] = studentUid,
                    ["termID"] = termId,
                    ["srOfferID"] = offeringId,
                    ["srofferIDListToCheck"] = otherOfferingIds
                }, token, cancellationToken))?.AsObject();
            if (conflict?["bConflictReturnValue"]?.GetValue<bool>() == true)
            {
                var message = ReadString(conflict["strConflictMessage"]);
                throw new CourseRegistrationValidationException(string.IsNullOrWhiteSpace(message)
                    ? "This course conflicts with another course on your schedule."
                    : $"This course has a schedule conflict. {message.Trim()}");
            }
        }

        var repeat = await SendForDataNodeAsync(HttpMethod.Post,
            "api/academic/register/check-repeats-validation", new JsonObject
            {
                ["department"] = ReadString(offering["department"]),
                ["courseID"] = ReadString(offering["courseID"]),
                ["courseType"] = ReadString(offering["courseType"]),
                ["courseName"] = ReadString(offering["coursename"]),
                ["studentUID"] = studentUid,
                ["termID"] = termId
            }, token, cancellationToken);
        var repeatMessage = ReadString(repeat);
        if (!string.IsNullOrWhiteSpace(repeatMessage) && hasNonWithdrawnCourseAttempt)
            throw new CourseRegistrationValidationException(
                "This course appears to repeat coursework you have already completed. Please contact your advisor if you need to take it again.");
    }

    private async Task CancelRegistrationSessionAsync(
        int studentUid, int termId, string token, CancellationToken cancellationToken)
    {
        try
        {
            await SendForDataNodeAsync(HttpMethod.Delete,
                $"api/academic/register/cancel/{termId}/{studentUid}", null, token, cancellationToken);
        }
        catch
        {
            // Preserve the original operation failure. Elements semaphore cleanup can be retried by a later session.
        }
    }

    private static JsonObject BuildNewRegistration(
        JsonObject offer, JsonArray registeredCourses, int studentUid, int termId)
    {
        var offerGpaGroupId = ReadInt(offer["gpaGroupID"]);
        var template = registeredCourses
            .FirstOrDefault(x => ReadInt(x?["gpaGroupID"]) == offerGpaGroupId)
            ?.AsObject();
        var course = template?.DeepClone().AsObject() ?? new JsonObject();
        foreach (var name in new[]
                 {
                     "department", "courseID", "courseType", "section", "credits", "varCredits",
                     "startDate", "contactHours", "costCenter", "costCenterID", "refundGroupID",
                     "gpaGroupID", "writingIntensive", "honors", "gradeCatalogID", "excludeRepeatCharge",
                     "locationID", "allFacultyScheduleNames"
                 })
        {
            if (offer.TryGetPropertyValue(name, out var value)) course[name] = value?.DeepClone();
        }
        course["courseName"] = offer["coursename"]?.DeepClone();
        course["completionDate"] = offer["endDate"]?.DeepClone();
        course["campusID"] = offer["accessCampusID"]?.DeepClone();
        course["studentUID"] = studentUid;
        course["termCalendarID"] = termId;
        course["srOfferID"] = offer["srOfferID"]?.DeepClone();
        course["srAcademicID"] = 0;
        if (template is null) course["studentStatusID"] = 0;
        course["registrationStatus"] = RegistrationMode;
        course["courseStatus"] = "ADD";
        course["grade"] = "";
        course["clockHours"] = offer["credits"]?.DeepClone();
        course["facultyName"] = offer["allFacultyScheduleNames"]?.DeepClone();
        course["facultyID"] = null;
        course["effectiveAddDate"] = DateTime.UtcNow;
        course["effectiveWithdrawDate"] = null;
        return course;
    }

    private static JsonObject? FindRegistration(JsonArray courses, int offeringId) =>
        courses.SingleOrDefault(x => ReadInt(x?["srOfferID"]) == offeringId &&
                                     !string.Equals(x?["courseStatus"]?.GetValue<string>(), "WITHDRAWN",
                                         StringComparison.OrdinalIgnoreCase))?.AsObject();

    private async Task<CourseSelectionRegistration?> WaitForRegistrationAsync(
        string externalStudentId, string externalTermId, string externalOfferingId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var match = (await _lookup.GetRegistrationsAsync(
                    externalStudentId, externalTermId, cancellationToken))
                .SingleOrDefault(x => x.ExternalOfferingId == externalOfferingId &&
                                      x.EffectiveWithdrawDate is null);
            if (match is not null) return match;
            if (attempt < 9) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return null;
    }

    private async Task<bool> WaitForRegistrationRemovalAsync(
        string externalStudentId, string externalTermId, string externalRegistrationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var registrations = await _lookup.GetRegistrationsAsync(
                externalStudentId, externalTermId, cancellationToken);
            if (registrations.All(x => x.ExternalRegistrationId != externalRegistrationId ||
                                       x.EffectiveWithdrawDate is not null)) return true;
            if (attempt < 9) await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return false;
    }

    private async Task<JsonObject> SendForDataAsync(
        HttpMethod method, string relativeUrl, JsonObject? body, string token,
        CancellationToken cancellationToken)
    {
        var data = await SendForDataNodeAsync(method, relativeUrl, body, token, cancellationToken);
        return data?.AsObject()
               ?? throw new InvalidOperationException("Elements returned an unexpected registration response.");
    }

    private async Task<JsonNode?> SendForDataNodeAsync(
        HttpMethod method, string relativeUrl, JsonObject? body, string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateRegistrationRequest(method, relativeUrl, token);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var diagnostic = string.IsNullOrWhiteSpace(content)
                ? "The gateway returned an empty response."
                : content.Length <= 1000 ? content : content[..1000];
            throw new HttpRequestException(
                $"Elements Registration returned {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                $"Response: {diagnostic}", null, response.StatusCode);
        }
        var envelope = JsonNode.Parse(content)?.AsObject()
                       ?? throw new InvalidOperationException("Elements returned an empty registration response.");
        if (envelope["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException("Elements did not accept the course registration operation.");
        return envelope["data"];
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;
        using var request = CreateIntegrationRequest(HttpMethod.Get, "api/Login/Authenticate");
        var credentials = $"{GetRequiredSetting("Username")}:{GetRequiredSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Elements authentication did not return a token.");
        _memoryCache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateIntegrationRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method,
            $"{GetRequiredSetting("BaseUrl").TrimEnd('/')}/{relativeUrl.TrimStart('/')}");
        request.Headers.Add("TenantHost", GetRequiredSetting("TenantHost"));
        return request;
    }

    private HttpRequestMessage CreateRegistrationRequest(HttpMethod method, string relativeUrl, string token)
    {
        var baseUrl = GetRequiredSetting("RegistrationBaseUrl").TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}/{relativeUrl.TrimStart('/')}");
        var tenantHost = GetRequiredSetting("TenantHost").TrimEnd('/');
        var tenantOrigin = $"https://{tenantHost}";
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (baseUrl.Contains("azurewebsites.net", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("TenantHost", tenantHost);
        }
        else
        {
            request.Headers.Add("Module", "Registration");
            request.Headers.Add("Origin", tenantOrigin);
            request.Headers.Referrer = new Uri(tenantOrigin);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private string GetRequiredSetting(string name)
    {
        var value = _configuration[$"{ConfigurationPath}:{name}"];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.")
            : value;
    }

    private static int ParseId(string value, string parameterName) =>
        int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException("The Thesis Elements identifier is invalid.", parameterName);

    private static int ReadInt(JsonNode? value) => value?.GetValue<int>() ?? 0;
    private static decimal ReadDecimal(JsonNode? value) => value?.GetValue<decimal>() ?? 0m;
    private static string ReadString(JsonNode? value) => value?.GetValue<string>() ?? string.Empty;

    private sealed record RegistrationState(
        int TermId, JsonObject Offering, JsonArray RegisteredCourses);
}
