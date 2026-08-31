using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.StudentInformationSystems;

public sealed class ThesisElementsDegreeAuditLookup :
    IStudentInformationSystemDegreeAuditLookup,
    ITransientDependency
{
    private const string ConnectionStringName = "ThesisElementsReadOnly";
    private const string ApiConfigurationPath = "StudentInformationSystems:Providers:ThesisElements:Api";
    private const string IntegrationTokenCacheKey = "ThesisElements:DegreeAudit:IntegrationToken";
    private static readonly HttpClient HttpClient = new();
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public ThesisElementsDegreeAuditLookup(IConfiguration configuration, IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
    }

    public StudentInformationSystemProvider Provider => StudentInformationSystemProvider.ThesisElements;

    public async Task<IReadOnlyList<StudentDegreeAuditSummary>> GetAuditsAsync(
        string externalStudentId,
        CancellationToken cancellationToken = default)
    {
        var studentUid = ParseStudentId(externalStudentId);
        const string sql = """
            SELECT RevTermCalendarID, AuditDegreeID, AuditProgramID, Degree, Program,
                   RevisionTerm, CreditsRequired, CreditsCompleted, MinimumGPA,
                   GPAAttained, Status, NeedsUpdate
            FROM dbo.CAMS_StudentAuditProgram_View
            WHERE StudentUID = @StudentUID
            ORDER BY RevTermCalendarID DESC, Program
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var audits = new List<StudentDegreeAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            audits.Add(ReadSummary(reader));
        }

        return audits;
    }

    public async Task<StudentDegreeAuditDetail?> GetAuditAsync(
        string externalStudentId,
        int revisionTermId,
        int auditDegreeId,
        int auditProgramId,
        CancellationToken cancellationToken = default)
    {
        var studentUid = ParseStudentId(externalStudentId);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var summary = await FindSummaryAsync(
            connection, studentUid, revisionTermId, auditDegreeId, auditProgramId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var courses = new List<StudentDegreeAuditCourse>();
        // The legacy portal used Nelson's custom 3DTech reporting procedure. That
        // procedure is not deployed to the Elements read replica, so compose the
        // same read-only result from the standard Elements audit tables instead.
        const string detailSql = """
            SELECT DISTINCT
                R.[Name] AS ReqName,
                R.CreditsRequired AS ReqCrRequired,
                R.CreditsCompleted AS ReqCrCompleted,
                R.MinimumGPA AS ReqMinGPA,
                R.GPAAttained AS ReqGPAAttained,
                R.[Status] AS ReqStatus,
                R.SortOrder AS ReqSortOrder,
                G.[Name] AS GroupName,
                G.CreditsRequired AS GrpCrRequired,
                G.CreditsCompleted AS GrpCrCompleted,
                G.MinimumGPA AS GrpMinGPA,
                G.GPAAttained AS GrpGPAAttained,
                G.[Status] AS GrpStatus,
                G.SortOrder AS GrpSortOrder,
                C.Department + C.CourseID + C.CourseType AS AuditCourse,
                CAST(C.CourseName AS nvarchar(240)) AS CourseName,
                CASE
                    WHEN M.Completed = 'Yes' AND M.[Status] = 'Transfer' THEN 'TC'
                    WHEN M.Completed = 'No' AND M.[Status] = 'Transfer' THEN 'TR'
                    WHEN M.Completed = 'Yes' AND M.[Status] = 'Manual' THEN 'MC'
                    WHEN M.Completed = 'No' AND M.[Status] = 'Manual' THEN 'MR'
                    WHEN M.Completed = 'Yes' THEN 'C'
                    WHEN M.[Status] = 'In Progress' THEN 'InP'
                    WHEN C.Completed = 'Yes' THEN 'C'
                    WHEN C.Completed = 'No' THEN 'R'
                    ELSE 'NN'
                END AS CrsStatus,
                COALESCE(A.Grade, '') AS Grade,
                COALESCE(A.Credits, C.Credits, 0) AS Credits,
                COALESCE(T.TextTerm, '') AS Term,
                COALESCE(A.Department, '') AS Department,
                COALESCE(A.CourseID, '') AS CourseID,
                COALESCE(A.CourseType, '') AS CourseType,
                COALESCE(A.[Section], '') AS [Section]
            FROM dbo.StudentAudReq R
            INNER JOIN dbo.StudentAudGrp G
                ON G.StudentUID = R.StudentUID
               AND G.RevTermCalendarID = R.RevTermCalendarID
               AND G.AuditDegreeID = R.AuditDegreeID
               AND G.AuditProgramID = R.AuditProgramID
               AND G.AuditRequirementID = R.AuditRequirementID
            INNER JOIN dbo.StudentAudCrs C
                ON C.StudentUID = G.StudentUID
               AND C.RevTermCalendarID = G.RevTermCalendarID
               AND C.AuditDegreeID = G.AuditDegreeID
               AND C.AuditProgramID = G.AuditProgramID
               AND C.AuditRequirementID = G.AuditRequirementID
               AND C.AuditGroupID = G.AuditGroupID
            LEFT JOIN dbo.StudentAudCrsMatches M
                ON M.StudentAudCrsID = C.StudentAudCrsID
            LEFT JOIN dbo.SRAcademic A
                ON A.SRAcademicID = M.SRAcademicIDMatch
            LEFT JOIN dbo.TermCalendar T
                ON T.TermCalendarID = A.TermCalendarID
            WHERE R.StudentUID = @StudentUID
              AND R.RevTermCalendarID = @RevTermCalendarID
              AND R.AuditDegreeID = @AuditDegreeID
              AND R.AuditProgramID = @AuditProgramID
            ORDER BY R.SortOrder, R.[Name], G.SortOrder, G.[Name], AuditCourse
            """;
        await using (var command = new SqlCommand(detailSql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 120
        })
        {
            command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
            command.Parameters.Add("@RevTermCalendarID", SqlDbType.Int).Value = revisionTermId;
            command.Parameters.Add("@AuditDegreeID", SqlDbType.Int).Value = auditDegreeId;
            command.Parameters.Add("@AuditProgramID", SqlDbType.Int).Value = auditProgramId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                courses.Add(new StudentDegreeAuditCourse(
                    Text(reader, "ReqName"), Decimal(reader, "ReqCrRequired"),
                    Decimal(reader, "ReqCrCompleted"), Decimal(reader, "ReqMinGPA"),
                    Decimal(reader, "ReqGPAAttained"), Text(reader, "ReqStatus"),
                    Integer(reader, "ReqSortOrder"), Text(reader, "GroupName"),
                    Decimal(reader, "GrpCrRequired"), Decimal(reader, "GrpCrCompleted"),
                    Decimal(reader, "GrpMinGPA"), Decimal(reader, "GrpGPAAttained"),
                    Text(reader, "GrpStatus"), Integer(reader, "GrpSortOrder"),
                    Text(reader, "AuditCourse"), Text(reader, "CourseName"),
                    Text(reader, "CrsStatus"), Text(reader, "Grade"),
                    Decimal(reader, "Credits"), Text(reader, "Term"),
                    $"{Text(reader, "Department")}{Text(reader, "CourseID")}{Text(reader, "CourseType")}{Text(reader, "Section")}"));
            }
        }

        // Overall transcript GPA is calculated by an Elements procedure for which
        // the replica's read-only login intentionally has no EXECUTE permission.
        // The degree-audit GPA remains available from the summary view; leave the
        // distinct overall GPA empty until it is exposed through a read-only API.
        return new StudentDegreeAuditDetail(summary, null, courses);
    }

    public async Task RefreshAuditAsync(
        string externalStudentId,
        int revisionTermId,
        int auditDegreeId,
        int auditProgramId,
        CancellationToken cancellationToken = default)
    {
        var studentUid = ParseStudentId(externalStudentId);
        var token = await GetIntegrationAccessTokenAsync(cancellationToken);
        var route = "/api/degree-audit/student-evaluation/evaluate-program-details" +
                    $"/{studentUid.ToString(CultureInfo.InvariantCulture)}" +
                    $"/{auditProgramId.ToString(CultureInfo.InvariantCulture)}" +
                    $"/{auditDegreeId.ToString(CultureInfo.InvariantCulture)}" +
                    $"/{revisionTermId.ToString(CultureInfo.InvariantCulture)}";
        using var request = CreateRegistrationRequest(HttpMethod.Get, route, token);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureApiSuccess(response, content, "refresh the degree audit");
        var envelope = JsonNode.Parse(content)?.AsObject()
                       ?? throw new InvalidOperationException("Elements returned an empty degree audit response.");
        if (envelope["isSuccess"]?.GetValue<bool>() != true || envelope["data"] is null)
        {
            throw new InvalidOperationException("Elements did not complete the degree audit refresh.");
        }
    }

    private async Task<string> GetIntegrationAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(IntegrationTokenCacheKey, out string? cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{GetRequiredApiSetting("BaseUrl").TrimEnd('/')}/api/Login/Authenticate");
        request.Headers.Add("TenantHost", GetRequiredApiSetting("TenantHost"));
        var credentials = $"{GetRequiredApiSetting("Username")}:{GetRequiredApiSetting("Password")}";
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureApiSuccess(response, content, "authenticate with the Elements API");
        var token = JsonNode.Parse(content)?["data"]?["token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Elements authentication did not return a token.");
        }

        _memoryCache.Set(IntegrationTokenCacheKey, token, TimeSpan.FromMinutes(10));
        return token;
    }

    private HttpRequestMessage CreateRegistrationRequest(
        HttpMethod method,
        string relativeUrl,
        string token)
    {
        var baseUrl = GetRequiredApiSetting("RegistrationBaseUrl").TrimEnd('/');
        var tenantHost = GetRequiredApiSetting("TenantHost").TrimEnd('/');
        var tenantOrigin = $"https://{tenantHost}";
        var request = new HttpRequestMessage(method, $"{baseUrl}/{relativeUrl.TrimStart('/')}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Module", "Registration");
        request.Headers.Add("Origin", tenantOrigin);
        request.Headers.Referrer = new Uri(tenantOrigin);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static void EnsureApiSuccess(
        HttpResponseMessage response,
        string content,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(content)
            ? "The response did not include an explanation."
            : content.Length <= 1000 ? content : content[..1000];
        throw new HttpRequestException(
            $"Unable to {operation}. Elements returned " +
            $"{(int)response.StatusCode} ({response.ReasonPhrase}). Response: {detail}",
            null,
            response.StatusCode);
    }

    private string GetRequiredApiSetting(string name)
    {
        var value = _configuration[$"{ApiConfigurationPath}:{name}"];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thesis Elements API setting '{name}' is not configured.")
            : value;
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        }

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<StudentDegreeAuditSummary?> FindSummaryAsync(
        SqlConnection connection, int studentUid, int revisionTermId, int auditDegreeId,
        int auditProgramId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT RevTermCalendarID, AuditDegreeID, AuditProgramID, Degree, Program,
                   RevisionTerm, CreditsRequired, CreditsCompleted, MinimumGPA,
                   GPAAttained, Status, NeedsUpdate
            FROM dbo.CAMS_StudentAuditProgram_View
            WHERE StudentUID = @StudentUID AND RevTermCalendarID = @RevTermCalendarID
              AND AuditDegreeID = @AuditDegreeID AND AuditProgramID = @AuditProgramID
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@StudentUID", SqlDbType.Int).Value = studentUid;
        command.Parameters.Add("@RevTermCalendarID", SqlDbType.Int).Value = revisionTermId;
        command.Parameters.Add("@AuditDegreeID", SqlDbType.Int).Value = auditDegreeId;
        command.Parameters.Add("@AuditProgramID", SqlDbType.Int).Value = auditProgramId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSummary(reader) : null;
    }

    private static StudentDegreeAuditSummary ReadSummary(SqlDataReader reader) => new(
        Integer(reader, "RevTermCalendarID"), Integer(reader, "AuditDegreeID"),
        Integer(reader, "AuditProgramID"), Text(reader, "Degree"), Text(reader, "Program"),
        Text(reader, "RevisionTerm"), Decimal(reader, "CreditsRequired"),
        Decimal(reader, "CreditsCompleted"), Decimal(reader, "MinimumGPA"),
        Decimal(reader, "GPAAttained"), Text(reader, "Status"), Boolean(reader, "NeedsUpdate"));

    private static int ParseStudentId(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new InvalidOperationException("The Elements student identifier is invalid.");

    private static string Text(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;
    }

    private static int Integer(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static decimal Decimal(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static bool Boolean(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }
}
