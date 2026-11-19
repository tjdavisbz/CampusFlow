using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.AccountRecovery;

/// <summary>
/// Confirms current Elements identity and uses separately imported legacy verification proof.
/// The public recovery request never connects to the legacy CAMS server or reads an answer.
/// </summary>
[DisableAuditing]
[ExposeServices(typeof(IAccountRecoveryIdentitySource))]
public sealed class LegacyAccountRecoveryIdentitySource : IAccountRecoveryIdentitySource, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IAccountRecoveryVerificationStore _verifications;
    private readonly Func<string, SqlCommand, CancellationToken, Task<IReadOnlyList<object?[]>>> _read;

    public LegacyAccountRecoveryIdentitySource(IConfiguration configuration, IAccountRecoveryVerificationStore verifications)
        : this(configuration, verifications, ReadAsync) { }

    internal LegacyAccountRecoveryIdentitySource(IConfiguration configuration,
        IAccountRecoveryVerificationStore verifications,
        Func<string, SqlCommand, CancellationToken, Task<IReadOnlyList<object?[]>>> read)
    {
        _configuration = configuration;
        _verifications = verifications;
        _read = read;
    }

    public bool IsConfigured => TryConnection(out _);

    public async Task<RecoveryPerson?> VerifyIdentityAsync(AccountRecoveryIdentity identity, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!TryConnection(out var connection)) return null;
            using var command = CreateIdentityCommand(identity);
            var candidate = ReadUniqueIdentity(await _read(connection, command, ct));
            if (candidate is null) return null;
            var proof = await _verifications.GetAsync(candidate.Value.StudentUid, ct);
            if (!ValidProof(proof, candidate.Value.StudentUid)) return null;
            return new RecoveryPerson(candidate.Value.StudentUid, proof!.LegacyStudentUid, candidate.Value.Upn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return null; } // Neither personal data nor SQL/provider errors reach the public flow.
    }

    public async Task<RecoveryQuestion> GetQuestionAsync(RecoveryPerson person, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!TryConnection(out _) || !ValidPerson(person)) return Unavailable();
            var proof = await _verifications.GetAsync(person.StudentUid, ct);
            if (!ValidProof(proof, person.StudentUid) || proof!.LegacyStudentUid != person.LegacyStudentUid)
                return Unavailable();
            return new RecoveryQuestion(true, proof.QuestionId, proof.Question, proof.LegacyNoQuestionAllowed, proof.SourceFingerprint);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    public async Task<bool> RevalidatePersonAsync(RecoveryPerson person, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!TryConnection(out var connection) || !ValidPerson(person)) return false;
            using var command = new SqlCommand("SELECT TOP (2) s.StudentUID, recoveryEmail.UserPrincipalName " + RecoveryStudentEmailSql.From +
                " WHERE s.StudentUID=@StudentUid AND " + RecoveryStudentEmailSql.UniqueOwner + " ORDER BY s.StudentUID;") { CommandTimeout = 10 };
            command.Parameters.Add("@StudentUid", SqlDbType.Int).Value = person.StudentUid;
            var current = ReadUniqueIdentity(await _read(connection, command, ct));
            return current is not null && current.Value.StudentUid == person.StudentUid &&
                string.Equals(current.Value.Upn, person.UserPrincipalName, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    public async Task<bool> VerifyAnswerAsync(RecoveryPerson person, int questionId, string answer, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!TryConnection(out _) || !ValidPerson(person) || questionId <= 0 ||
                string.IsNullOrWhiteSpace(answer) || answer.Length > 500) return false;
            var proof = await _verifications.GetAsync(person.StudentUid, ct);
            return ValidProof(proof, person.StudentUid) && proof!.LegacyStudentUid == person.LegacyStudentUid &&
                proof.QuestionId == questionId && !proof.LegacyNoQuestionAllowed &&
                RecoveryAnswerHash.Verify(answer, proof.AnswerHash);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    private bool TryConnection(out string connection)
    {
        connection = "";
        try
        {
            if (!bool.TryParse(_configuration["PasswordRecovery:VerificationImportReady"], out var ready) || !ready)
                return false;
            connection = BuildCurrentConnectionString(_configuration);
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static string BuildCurrentConnectionString(IConfiguration configuration)
    {
        var label = configuration["App:EnvironmentLabel"];
        var nonprod = string.Equals(label, "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase);
        var production = string.IsNullOrWhiteSpace(label) ||
            string.Equals(label, "PRODUCTION", StringComparison.OrdinalIgnoreCase);
        if (!nonprod && (!production || configuration["ASPNETCORE_ENVIRONMENT"] != "Production"))
            throw new InvalidOperationException("The recovery source environment is not verified.");

        var value = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("The recovery source is not configured.");
        var builder = new SqlConnectionStringBuilder(value);
        var server = nonprod ? "sql-elm-nonprod-usc-replica.database.windows.net" :
            "sql-elm-prod-usc-replica.database.windows.net";
        if (!(string.Equals(builder.DataSource, server, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(builder.DataSource, "tcp:" + server + ",1433", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(builder.InitialCatalog, nonprod ? "EL-NP-NEL-DB" : "EL-NEL-DB", StringComparison.OrdinalIgnoreCase) ||
            builder.TrustServerCertificate || builder.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("The recovery source must be the matching trusted TLS replica.");
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        builder.PersistSecurityInfo = false;
        builder.ConnectTimeout = builder.ConnectTimeout is > 0 and < 10 ? builder.ConnectTimeout : 10;
        builder.ConnectRetryCount = 0;
        return builder.ConnectionString;
    }

    internal static SqlCommand CreateIdentityCommand(AccountRecoveryIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var first = identity.FirstName?.Trim() ?? "";
        var last = identity.LastName?.Trim() ?? "";
        if (first.Length is < 1 or > 100 || last.Length is < 1 or > 100 ||
            first.Any(char.IsControl) || last.Any(char.IsControl) || identity.BirthDate == default ||
            identity.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Enter valid identity fields.");
        var ssnInput = identity.Ssn?.Trim() ?? "";
        if (ssnInput.Any(x => !char.IsAsciiDigit(x) && x != '-' && !char.IsWhiteSpace(x)))
            throw new ArgumentException("Enter a valid identity field.");
        var ssn = new string(ssnInput.Where(char.IsAsciiDigit).ToArray());
        if (ssn.Length != 9) throw new ArgumentException("Enter a valid identity field.");

        var command = new SqlCommand("SELECT TOP (2) s.StudentUID, recoveryEmail.UserPrincipalName " + RecoveryStudentEmailSql.From + " " + """
            WHERE LTRIM(RTRIM(s.FirstName)) = @FirstName
              AND LTRIM(RTRIM(s.LastName)) = @LastName
              AND REPLACE(s.StudentSSN, '-', '') = @Ssn
              AND CAST(s.BirthDate AS date) = @BirthDate
              AND
            """ + " " + RecoveryStudentEmailSql.UniqueOwner + " ORDER BY s.StudentUID;") { CommandTimeout = 10 };
        command.Parameters.Add("@FirstName", SqlDbType.NVarChar, 100).Value = first;
        command.Parameters.Add("@LastName", SqlDbType.NVarChar, 100).Value = last;
        command.Parameters.Add("@Ssn", SqlDbType.VarChar, 9).Value = ssn;
        command.Parameters.Add("@BirthDate", SqlDbType.Date).Value = identity.BirthDate.ToDateTime(TimeOnly.MinValue);
        return command;
    }

    internal static (int StudentUid, string Upn)? ReadUniqueIdentity(IReadOnlyList<object?[]> rows)
    {
        if (rows.Count != 1 || rows[0].Length != 2 || rows[0][0] is not int uid || uid <= 0 ||
            rows[0][1] is not string email || NormalizeInstitutionalUpn(email) is not string upn)
            return null;
        return (uid, upn);
    }

    internal static string? NormalizeInstitutionalUpn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > 254 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return null;
        try
        {
            var address = new MailAddress(value);
            return address.Host.Equals("my.nelson.edu", StringComparison.OrdinalIgnoreCase) &&
                address.Address.Equals(value, StringComparison.OrdinalIgnoreCase) && address.DisplayName.Length == 0
                ? value.ToLowerInvariant() : null;
        }
        catch (FormatException) { return null; }
    }

    private static bool ValidPerson(RecoveryPerson? person) => person is not null && person.StudentUid > 0 &&
        person.LegacyStudentUid > 0 && NormalizeInstitutionalUpn(person.UserPrincipalName) is not null;

    private static bool ValidProof(RecoveryVerification? proof, int studentUid)
    {
        if (proof is null || proof.IsBlocked || proof.StudentUid != studentUid || proof.LegacyStudentUid <= 0 ||
            proof.TenantId is not Guid tenant || tenant == Guid.Empty || proof.SourceFingerprint.Length != 64 ||
            proof.SourceFingerprint.Any(x => !char.IsAsciiHexDigit(x))) return false;
        return proof.QuestionId.HasValue
            ? proof.QuestionId > 0 && !string.IsNullOrWhiteSpace(proof.Question) && proof.Question.Length <= 500 &&
              RecoveryAnswerHash.IsValid(proof.AnswerHash) && !proof.LegacyNoQuestionAllowed
            : proof.LegacyNoQuestionAllowed && proof.Question is null && proof.AnswerHash is null;
    }

    private static RecoveryQuestion Unavailable() => new(false, null, null, false);

    private static async Task<IReadOnlyList<object?[]>> ReadAsync(string connectionString, SqlCommand command,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        command.Connection = connection;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values.Select(x => x is DBNull ? null : x).ToArray());
        }
        return rows;
    }
}
