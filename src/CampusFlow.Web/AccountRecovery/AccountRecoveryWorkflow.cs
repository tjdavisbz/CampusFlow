using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AccountRecovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Web.AccountRecovery;

[DisableAuditing]
public sealed class AccountRecoveryWorkflow(IConfiguration config, IAccountRecoveryIdentitySource identity,
    IAccountRecoveryDirectory directory, IAccountRecoveryRequestStore requests, ICurrentTenant tenant,
    ITenantStore tenants, ITenantNormalizer normalizer, ILogger<AccountRecoveryWorkflow> logger,
    TimeProvider? time = null) : IAccountRecoveryWorkflow, ITransientDependency
{
    private DateTime Now => (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    public bool IsAvailable
    {
        get
        {
            if (!config.GetValue<bool>("PasswordRecovery:Enabled")) return false;
            var imported = config.GetValue<bool>("PasswordRecovery:VerificationImportReady");
            var tenantConfigured = Guid.TryParse(config["PasswordRecovery:CampusFlowTenantId"], out var id) && id != Guid.Empty;
            var identityReady = identity.IsConfigured;
            var directoryReady = directory.IsConfigured;
            var proxyReady = !RecoveryClientAddress.RequiresTrustedProxy(config) || RecoveryClientAddress.HasConfiguration(config);
            var ready = imported && tenantConfigured && identityReady && directoryReady && proxyReady;
            if (!ready)
                logger.LogWarning("Account recovery setup blocked: ImportReady={ImportReady}, TenantConfigured={TenantConfigured}, IdentitySourceReady={IdentitySourceReady}, DirectoryReady={DirectoryReady}, ProxyReady={ProxyReady}, DirectoryBackend={DirectoryBackend}",
                    imported, tenantConfigured, identityReady, directoryReady, proxyReady, directory.GetType().Name);
            return ready;
        }
    }
    private static AccountRecoveryResult Result(RecoveryStep step, RecoveryNotice notice = RecoveryNotice.None, string? token = null, string? question = null)
        => new() { Step = step, Notice = notice, Ticket = token, Question = question };
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool ValidTicket(string? token) => token is not null && Regex.IsMatch(token, "^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);
    private async Task<IDisposable?> Scope()
    {
        if (!IsAvailable) return null;
        TenantConfiguration? named;
        using (tenant.Change(null)) named = await tenants.FindAsync(normalizer.NormalizeName("nelson") ?? "");
        if (named is null || named.Id != Guid.Parse(config["PasswordRecovery:CampusFlowTenantId"]!) || !string.Equals(named.Name,"nelson",StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Account recovery tenant guard blocked: TenantFound={TenantFound}, TenantIdMatches={TenantIdMatches}, TenantNameMatches={TenantNameMatches}",
                named is not null, named?.Id == Guid.Parse(config["PasswordRecovery:CampusFlowTenantId"]!), string.Equals(named?.Name, "nelson", StringComparison.OrdinalIgnoreCase));
            return null;
        }
        return tenant.Change(named.Id, named.Name);
    }
    private async Task<AccountRecoveryResult> Safe(Func<Task<AccountRecoveryResult>> action)
    {
        try { using var scope = await Scope(); return scope is null ? Result(RecoveryStep.Unavailable) : await action(); }
        catch (OperationCanceledException) { throw; }
        catch { logger.LogWarning("Account recovery could not complete a verification step. No sensitive inputs were logged."); return Result(RecoveryStep.Unavailable); }
    }
    private async Task<RecoveryRequest?> Read(string? token, string address, CancellationToken ct)
    {
        if (!ValidTicket(token)) return null;
        var row = await requests.FindAsync(Digest(token!), ct);
        return row is not null && row.Active(Now) && row.ClientHash == Digest(address) ? row : null;
    }
    private bool Allowed(RecoveryDirectoryAccount? account, RecoveryPerson person) => account is not null && account.Enabled &&
        account.IsMember && !account.IsPrivileged && (!account.IsSynchronized || directory.SupportsSynchronizedAccounts) && account.ObjectId != Guid.Empty &&
        string.Equals(account.UserPrincipalName, person.UserPrincipalName, StringComparison.OrdinalIgnoreCase);
    private static bool SameProof(RecoveryRequest row, RecoveryQuestion proof) => proof.SourceAvailable &&
        proof.SourceFingerprint is { Length: 64 } fingerprint && fingerprint == row.VerificationFingerprint &&
        proof.Id == row.QuestionId && (proof.Id.HasValue ? !proof.MayContinueWithoutQuestion && !string.IsNullOrWhiteSpace(proof.Text)
            : proof.MayContinueWithoutQuestion);
    private async Task<AccountRecoveryResult> View(RecoveryRequest row, string token, CancellationToken ct)
    {
        if ((row.State is "Question" or "Verified") && row.Person is RecoveryPerson person)
        {
            var q = await identity.GetQuestionAsync(person, ct);
            if (!SameProof(row, q)) { await requests.RejectAsync(row.Id, ct); return Result(RecoveryStep.Unavailable); }
            return row.State == "Question" ? Result(RecoveryStep.Question, token: token, question: q.Text)
                : Result(RecoveryStep.Password, token: token);
        }
        return row.State switch { "Verified" => Result(RecoveryStep.Password, token: token), "Complete" => Result(RecoveryStep.Complete),
            "Applying" or "Unknown" => Result(RecoveryStep.Unknown), _ => Result(RecoveryStep.Identity, RecoveryNotice.NotVerified) };
    }
    public Task<AccountRecoveryResult> ResumeAsync(string? ticket, string clientAddress, CancellationToken ct = default)
        => Safe(async () => { var row = await Read(ticket, clientAddress, ct); return row is null ? Result(RecoveryStep.Identity, ticket is null ? RecoveryNotice.None : RecoveryNotice.Expired) : await View(row, ticket!, ct); });
    public Task<AccountRecoveryResult> StartAsync(AccountRecoveryIdentity input, string clientAddress, CancellationToken ct = default)
        => Safe(async () => {
            if (input is null || string.IsNullOrWhiteSpace(input.FirstName) || input.FirstName.Length > 100 ||
                string.IsNullOrWhiteSpace(input.LastName) || input.LastName.Length > 100 || !Regex.IsMatch(input.Ssn ?? "", "^[0-9]{9}$", RegexOptions.CultureInvariant) ||
                input.BirthDate < new DateOnly(1900,1,1) || input.BirthDate > DateOnly.FromDateTime(Now)) return Result(RecoveryStep.Identity, RecoveryNotice.NotVerified);
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
            var row = new RecoveryRequest(Guid.NewGuid(), tenant.Id!.Value, Digest(token), Digest(clientAddress), Now);
            if (!await requests.AddAsync(row, Now, ct)) return Result(RecoveryStep.Identity, RecoveryNotice.RateLimited);
            var person = await identity.VerifyIdentityAsync(input, ct);
            if (person is null) { await requests.RejectAsync(row.Id, ct); return Result(RecoveryStep.Identity, RecoveryNotice.NotVerified); }
            var question = await identity.GetQuestionAsync(person, ct);
            if (!question.SourceAvailable || question.SourceFingerprint is not { Length: 64 } || (!question.Id.HasValue && !question.MayContinueWithoutQuestion))
            { await requests.RejectAsync(row.Id,ct); return Result(RecoveryStep.Identity, RecoveryNotice.NotVerified); }
            var account = await directory.FindAsync(person.UserPrincipalName, ct);
            if (!Allowed(account, person)) { await requests.RejectAsync(row.Id,ct); return Result(RecoveryStep.Unavailable); }
            if (!await requests.BindAsync(row.Id, person, account!, question, Now, ct)) return Result(RecoveryStep.Identity, RecoveryNotice.RateLimited);
            var bound = await requests.FindAsync(Digest(token), ct) ?? throw new InvalidOperationException("Recovery state missing.");
            return await View(bound, token, ct);
        });
    public Task<AccountRecoveryResult> AnswerAsync(string? ticket, string answer, string clientAddress, CancellationToken ct = default)
        => Safe(async () => {
            var row = await Read(ticket, clientAddress,ct);
            if (row?.Person is not RecoveryPerson person || row.QuestionId is not int q || string.IsNullOrWhiteSpace(answer) || answer.Length > 500)
                return Result(RecoveryStep.Identity, RecoveryNotice.NotVerified);
            if (!SameProof(row, await identity.GetQuestionAsync(person, ct)))
            { await requests.RejectAsync(row.Id, ct); return Result(RecoveryStep.Unavailable); }
            if (!await requests.BeginAnswerAsync(row.Id, Now,ct)) return Result(RecoveryStep.Identity, RecoveryNotice.RateLimited);
            var verified = await identity.VerifyAnswerAsync(person,q,answer,ct);
            await requests.FinishAnswerAsync(row.Id,verified,ct);
            var fresh = await requests.FindAsync(Digest(ticket!),ct) ?? throw new InvalidOperationException("Recovery state missing.");
            var view = await View(fresh,ticket!,ct);
            return verified ? view : new AccountRecoveryResult { Step=view.Step,Ticket=view.Ticket,Question=view.Question,Notice=RecoveryNotice.NotVerified };
        });
    public Task<AccountRecoveryResult> ResetAsync(string? ticket, string password, string confirmation, string clientAddress, CancellationToken ct = default)
        => Safe(async () => {
            var row = await Read(ticket,clientAddress,ct);
            if (row?.Person is not RecoveryPerson person || row.State != "Verified")
                return row?.State is "Applying" or "Unknown" ? Result(RecoveryStep.Unknown) : Result(RecoveryStep.Identity,RecoveryNotice.Expired);
            if (string.IsNullOrEmpty(password) || password != confirmation)
                return Result(RecoveryStep.Password,RecoveryNotice.PasswordMismatch,ticket);
            if (password.Length is < 8 or > 256)
                return Result(RecoveryStep.Password,RecoveryNotice.PasswordPolicy,ticket);
            if (!await identity.RevalidatePersonAsync(person, ct) || !SameProof(row, await identity.GetQuestionAsync(person, ct)))
            { await requests.RejectAsync(row.Id, ct); return Result(RecoveryStep.Unavailable); }
            var account = await directory.FindAsync(person.UserPrincipalName,ct);
            if (!Allowed(account,person) || account!.ObjectId != row.DirectoryObjectId)
            { await requests.RejectAsync(row.Id,ct);return Result(RecoveryStep.Unavailable); }
            if (!await requests.ClaimResetAsync(row.Id,Now,ct)) return Result(RecoveryStep.Unknown);
            RecoveryPasswordWriteResult outcome;
            try { outcome=await directory.ResetAsync(account,password,ct); }
            catch { outcome=RecoveryPasswordWriteResult.Unknown; }
            // Once a write is claimed, persist its outcome independently of request cancellation.
            try { await requests.FinishAsync(row.Id,outcome,CancellationToken.None); }
            catch { logger.LogWarning("Account recovery outcome could not be recorded; the one-use request remains locked."); return Result(RecoveryStep.Unknown); }
            logger.LogInformation("Account recovery password operation finished with outcome {Outcome}.",outcome);
            if (outcome == RecoveryPasswordWriteResult.PolicyRejected)
            {
                var fresh = await Read(ticket, clientAddress, CancellationToken.None);
                return fresh?.State == "Verified" ? Result(RecoveryStep.Password, RecoveryNotice.PasswordPolicy, ticket)
                    : Result(RecoveryStep.Unavailable);
            }
            return outcome switch { RecoveryPasswordWriteResult.Accepted=>Result(RecoveryStep.Complete),
                RecoveryPasswordWriteResult.Unknown=>Result(RecoveryStep.Unknown),_=>Result(RecoveryStep.Unavailable) };
        });
}
