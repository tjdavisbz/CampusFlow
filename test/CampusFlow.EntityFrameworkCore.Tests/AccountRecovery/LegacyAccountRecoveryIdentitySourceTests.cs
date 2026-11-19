using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.AccountRecovery;

public sealed class LegacyAccountRecoveryIdentitySourceTests
{
    private const int CurrentUid = 42;
    private const int LegacyUid = 999;
    private static readonly string Hash = RecoveryAnswerHash.Create("Synthetic verification answer");
    private static AccountRecoveryIdentity Identity(string? first = null, string? ssn = null) => new()
    { FirstName = first ?? "Test", LastName = "O'Neal", Ssn = ssn ?? "123-45-6789", BirthDate = new DateOnly(1990, 2, 3) };
    private static RecoveryPerson Person() => new(CurrentUid, LegacyUid, "test@my.nelson.edu");
    private static RecoveryVerification Proof(bool blocked = false, bool noQuestion = false) => new(Guid.NewGuid(),
        Guid.NewGuid(), CurrentUid, LegacyUid, noQuestion ? null : 7, noQuestion ? null : "A synthetic question?",
        noQuestion ? null : Hash, noQuestion, blocked, new string('a', 64));
    private static IConfiguration Config(string? changed = null, string? value = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["App:EnvironmentLabel"] = "NON-PRODUCTION",
            ["PasswordRecovery:VerificationImportReady"] = "true",
            ["ConnectionStrings:ThesisElementsReadOnly"] = "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30"
        };
        if (changed is not null) settings[changed] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
    private static LegacyAccountRecoveryIdentitySource Source(Store store,
        IReadOnlyList<object?[]>? rows = null, IConfiguration? configuration = null,
        Func<string, SqlCommand, CancellationToken, Task<IReadOnlyList<object?[]>>>? read = null) =>
        new(configuration ?? Config(), store, read ?? ((_, _, _) => Task.FromResult(rows ?? [new object?[] { CurrentUid, "Test@my.nelson.edu" }])));

    [Fact]
    public void Identity_fields_are_typed_parameters_and_are_never_SQL_text()
    {
        using var command = LegacyAccountRecoveryIdentitySource.CreateIdentityCommand(Identity("Robert'); DROP TABLE Student;--"));
        Assert.Equal(4, command.Parameters.Count);
        Assert.DoesNotContain("Robert", command.CommandText);
        Assert.DoesNotContain("123456789", command.CommandText);
        Assert.Equal("Robert'); DROP TABLE Student;--", command.Parameters["@FirstName"].Value);
        Assert.Equal("O'Neal", command.Parameters["@LastName"].Value);
        Assert.Equal("123456789", command.Parameters["@Ssn"].Value);
        Assert.Equal(SqlDbType.Date, command.Parameters["@BirthDate"].SqlDbType);
        Assert.Equal(new DateTime(1990, 2, 3), command.Parameters["@BirthDate"].Value);
        Assert.Contains("TOP (2)", command.CommandText);
        Assert.Contains("other.StudentUID <> s.StudentUID", command.CommandText);
        Assert.Contains("other.Email1", command.CommandText);
        Assert.Contains("CAMS_StudentAddressList_View", command.CommandText);
        Assert.Contains("address.ActiveFlag = 'Yes'", command.CommandText);
        Assert.Contains("address.AddressType = 'Local'", command.CommandText);
        Assert.Contains("HAVING COUNT_BIG(*) = 1", command.CommandText);
        Assert.DoesNotContain("InstitutionalEmail", command.CommandText);
        Assert.DoesNotContain("PortalHandle", command.CommandText);
        Assert.DoesNotContain("PortalPassword", command.CommandText);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-45-6789")]
    [InlineData(" 123 45 6789 ")]
    public void Full_SSN_normalization_preserves_exact_digits(string input)
    { using var command = LegacyAccountRecoveryIdentitySource.CreateIdentityCommand(Identity(ssn: input)); Assert.Equal("123456789", command.Parameters["@Ssn"].Value); }

    [Theory]
    [InlineData("1234")]
    [InlineData("1234567890")]
    [InlineData("123-45-678x")]
    [InlineData("123456789' OR 1=1 --")]
    public void Partial_or_non_numeric_identifiers_are_rejected(string input)
    { Assert.Throws<ArgumentException>(() => LegacyAccountRecoveryIdentitySource.CreateIdentityCommand(Identity(ssn: input))); }

    [Fact]
    public async Task Mapping_keeps_current_and_legacy_IDs_independent()
    {
        var store = new Store { Value = Proof() };
        var found = await Source(store).VerifyIdentityAsync(Identity());
        Assert.Equal(new RecoveryPerson(CurrentUid, LegacyUid, "test@my.nelson.edu"), found);
        Assert.Equal(CurrentUid, store.RequestedUid);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public async Task Missing_or_ambiguous_current_matches_never_read_verification_proof(int count)
    {
        var store = new Store { Value = Proof() };
        var rows = Enumerable.Range(0, count).Select(_ => new object?[] { CurrentUid, "test@my.nelson.edu" }).ToArray();
        Assert.Null(await Source(store, rows).VerifyIdentityAsync(Identity())); Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("test@example.edu")]
    [InlineData("test@nelson.edu")] [InlineData("test@my.nelson.edu.evil.example")]
    [InlineData("Display Name <test@my.nelson.edu>")] [InlineData("test@my.nelson.edu\r\nother")]
    public async Task Only_authoritative_student_institutional_UPNs_are_eligible(string? upn)
    {
        var store = new Store { Value = Proof() };
        Assert.Null(await Source(store, [new object?[] { CurrentUid, upn }]).VerifyIdentityAsync(Identity()));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Missing_blocked_or_wrongly_owned_proof_is_not_a_no_question_bypass()
    {
        var store = new Store(); Assert.Null(await Source(store).VerifyIdentityAsync(Identity()));
        store.Value = Proof(blocked: true); Assert.Null(await Source(store).VerifyIdentityAsync(Identity()));
        store.Value = new RecoveryVerification(Guid.NewGuid(), Guid.NewGuid(), 43, LegacyUid, 7, "Question?", Hash, false, false, new string('a', 64));
        Assert.Null(await Source(store).VerifyIdentityAsync(Identity()));
    }

    [Fact]
    public async Task Imported_question_is_displayed_without_ever_returning_its_hash()
    {
        var question = await Source(new Store { Value = Proof() }).GetQuestionAsync(Person());
        Assert.True(question.SourceAvailable); Assert.Equal(7, question.Id);
        Assert.Equal(new string('a',64), question.SourceFingerprint);
        Assert.Equal("A synthetic question?", question.Text); Assert.False(question.MayContinueWithoutQuestion);
        Assert.DoesNotContain(Hash, question.ToString());
    }

    [Fact]
    public async Task Only_an_explicit_imported_no_question_proof_can_continue_without_an_answer()
    {
        var source = Source(new Store { Value = Proof(noQuestion: true) });
        var question = await source.GetQuestionAsync(Person());
        Assert.True(question.SourceAvailable); Assert.Null(question.Id); Assert.Null(question.Text);
        Assert.True(question.MayContinueWithoutQuestion);
        Assert.False(await source.VerifyAnswerAsync(Person(), 7, "Synthetic verification answer"));
    }

    [Fact]
    public async Task Answer_verification_uses_imported_PBKDF2_proof_and_rejects_wrong_or_changed_question()
    {
        var source = Source(new Store { Value = Proof() });
        Assert.True(await source.VerifyAnswerAsync(Person(), 7, "Synthetic verification answer"));
        Assert.False(await source.VerifyAnswerAsync(Person(), 7, "Incorrect answer"));
        Assert.False(await source.VerifyAnswerAsync(Person(), 8, "Synthetic verification answer"));
        Assert.False(await source.VerifyAnswerAsync(Person() with { LegacyStudentUid = 1000 }, 7, "Synthetic verification answer"));
    }

    [Fact]
    public async Task A_changed_import_mapping_blocks_both_question_and_answer()
    {
        var source = Source(new Store { Value = Proof() });
        var person = Person() with { LegacyStudentUid = 1000 };
        Assert.False((await source.GetQuestionAsync(person)).SourceAvailable);
        Assert.False(await source.VerifyAnswerAsync(person, 7, "Synthetic verification answer"));
    }

    [Theory]
    [InlineData("AnswerHash", "invalid")]
    [InlineData("SourceFingerprint", "bad")]
    [InlineData("Question", "")]
    public async Task Corrupt_imported_proof_fails_closed(string field, string value)
    {
        var proof = Proof(); typeof(RecoveryVerification).GetProperty(field)!.SetValue(proof, value);
        var source = Source(new Store { Value = proof });
        Assert.Null(await source.VerifyIdentityAsync(Identity()));
        Assert.False((await source.GetQuestionAsync(Person())).SourceAvailable);
        Assert.False(await source.VerifyAnswerAsync(Person(), 7, "Synthetic verification answer"));
    }

    [Fact]
    public async Task Source_and_store_errors_do_not_surface_provider_details()
    {
        var failedRead = Source(new Store(), read: (_, _, _) => throw new InvalidOperationException("Private source detail"));
        Assert.Null(await failedRead.VerifyIdentityAsync(Identity()));
        var failedStore = Source(new Store { Throw = true });
        Assert.Null(await failedStore.VerifyIdentityAsync(Identity()));
        Assert.False((await failedStore.GetQuestionAsync(Person())).SourceAvailable);
        Assert.False(await failedStore.VerifyAnswerAsync(Person(), 7, "Synthetic verification answer"));
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_attempting_a_read()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var store = new Store { Value = Proof() }; var source = Source(store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.VerifyIdentityAsync(Identity(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetQuestionAsync(Person(), cancellation.Token));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Final_mapping_recheck_is_parameterized_and_requires_the_same_unique_current_UPN()
    {
        var source = Source(new Store(), read: (_, command, _) => {
            Assert.Equal(CurrentUid, command.Parameters["@StudentUid"].Value);
            Assert.Single(command.Parameters.Cast<SqlParameter>());
            Assert.Contains("other.Email1", command.CommandText);
            Assert.DoesNotContain("StudentSSN", command.CommandText);
            return Task.FromResult<IReadOnlyList<object?[]>>([new object?[] { CurrentUid, "TEST@my.nelson.edu" }]);
        });
        Assert.True(await source.RevalidatePersonAsync(Person()));
        Assert.False(await Source(new Store(), [new object?[] { CurrentUid, "changed@my.nelson.edu" }]).RevalidatePersonAsync(Person()));
        Assert.False(await Source(new Store(), []).RevalidatePersonAsync(Person()));
        Assert.False(await Source(new Store(), [new object?[] { CurrentUid, "test@my.nelson.edu" }, new object?[] { CurrentUid, "test@my.nelson.edu" }]).RevalidatePersonAsync(Person()));
    }

    [Fact]
    public void Connection_is_pinned_read_only_and_preserves_trusted_TLS()
    {
        var b = new SqlConnectionStringBuilder(LegacyAccountRecoveryIdentitySource.BuildCurrentConnectionString(Config()));
        Assert.Equal(ApplicationIntent.ReadOnly, b.ApplicationIntent); Assert.False(b.PersistSecurityInfo);
        Assert.False(b.TrustServerCertificate); Assert.NotEqual(SqlConnectionEncryptOption.Optional, b.Encrypt);
        Assert.Equal(10, b.ConnectTimeout); Assert.Equal(0, b.ConnectRetryCount);
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(3, 3)] [InlineData(30, 10)]
    public void An_unlimited_connection_timeout_cannot_hold_public_requests_open(int supplied, int expected)
    {
        var configuration = Config("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;Encrypt=True;Connect Timeout=" + supplied);
        Assert.Equal(expected, new SqlConnectionStringBuilder(LegacyAccountRecoveryIdentitySource.BuildCurrentConnectionString(configuration)).ConnectTimeout);
    }

    [Theory]
    [InlineData("PasswordRecovery:VerificationImportReady", "false")]
    [InlineData("PasswordRecovery:VerificationImportReady", null)]
    [InlineData("App:EnvironmentLabel", "QA")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-prod-usc-replica.database.windows.net;Database=EL-NEL-DB")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NEL-DB")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net.evil.example;Database=EL-NP-NEL-DB")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;Encrypt=False")]
    [InlineData("ConnectionStrings:ThesisElementsReadOnly", "Server=sql-elm-nonprod-usc-replica.database.windows.net;Database=EL-NP-NEL-DB;TrustServerCertificate=True")]
    public async Task Unprepared_or_mixed_environments_cannot_read_identity_or_proof(string key, string? value)
    {
        var store = new Store { Value = Proof() }; var reads = 0;
        var source = Source(store, configuration: Config(key, value), read: (_, _, _) => { reads++; throw new InvalidOperationException(); });
        Assert.False(source.IsConfigured); Assert.Null(await source.VerifyIdentityAsync(Identity()));
        Assert.False((await source.GetQuestionAsync(Person())).SourceAvailable);
        Assert.False(await source.VerifyAnswerAsync(Person(), 7, "Synthetic verification answer"));
        Assert.Equal(0, reads); Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Production_requires_its_own_replica_and_production_runtime()
    {
        var values = new Dictionary<string, string?> {
            ["ASPNETCORE_ENVIRONMENT"] = "Production", ["PasswordRecovery:VerificationImportReady"] = "true",
            ["ConnectionStrings:ThesisElementsReadOnly"] = "Server=tcp:sql-elm-prod-usc-replica.database.windows.net,1433;Database=EL-NEL-DB;Encrypt=True" };
        Assert.True(Source(new Store(), configuration: new ConfigurationBuilder().AddInMemoryCollection(values).Build()).IsConfigured);
        values["ASPNETCORE_ENVIRONMENT"] = "Development";
        Assert.False(Source(new Store(), configuration: new ConfigurationBuilder().AddInMemoryCollection(values).Build()).IsConfigured);
    }

    private sealed class Store : IAccountRecoveryVerificationStore
    {
        public RecoveryVerification? Value; public int Calls; public int? RequestedUid; public bool Throw;
        public Task<RecoveryVerification?> GetAsync(int studentUid, CancellationToken ct = default)
        { Calls++; RequestedUid = studentUid; if (Throw) throw new InvalidOperationException("Private store detail"); return Task.FromResult(Value); }
    }
}
