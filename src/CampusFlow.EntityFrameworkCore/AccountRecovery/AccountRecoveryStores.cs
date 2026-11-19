using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Threading;

namespace CampusFlow.AccountRecovery;

public sealed class AccountRecoveryVerificationStore(IRepository<RecoveryVerification, Guid> rows,
    ICurrentTenant tenant, IUnitOfWorkManager units) : IAccountRecoveryVerificationStore, ITransientDependency
{
    public async Task<RecoveryVerification?> GetAsync(int studentUid, CancellationToken ct = default)
    {
        var owner = RecoveryStoreScope.Owner(tenant);
        using var unit = units.Begin(requiresNew: true, isTransactional: false);
        var list = await (await rows.GetQueryableAsync()).AsNoTracking().Where(x => x.TenantId == owner && x.StudentUid == studentUid).Take(2).ToListAsync(ct);
        if (list.Count > 1) throw new InvalidOperationException("Imported verification is ambiguous.");
        return list.SingleOrDefault();
    }
}

internal static class RecoveryStoreScope
{
    internal static Guid Owner(ICurrentTenant tenant) => tenant.Id is Guid id && id != Guid.Empty &&
        string.Equals(tenant.Name, "nelson", StringComparison.OrdinalIgnoreCase) ? id :
        throw new InvalidOperationException("Recovery requires the verified university scope.");
}

public sealed class AccountRecoveryRequestStore(IRepository<RecoveryRequest, Guid> rows,
    IDbContextProvider<CampusFlowDbContext> contexts, ICurrentTenant tenant, IUnitOfWorkManager units, ICancellationTokenProvider cancellationTokens, TimeProvider? time = null)
    : IAccountRecoveryRequestStore, ITransientDependency
{
    private DateTime Now => (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private async Task Lock(string resource, CancellationToken ct)
    {
        var db = await contexts.GetDbContextAsync();
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource},0))", ct);
    }
    public async Task<bool> AddAsync(RecoveryRequest request, DateTime now, CancellationToken ct = default)
    {
        var owner = RecoveryStoreScope.Owner(tenant);
        if (request.TenantId != owner) throw new InvalidOperationException("Recovery scope changed.");
        using var unit = units.Begin(requiresNew: true, isTransactional: true);
        await Lock($"Recovery:{owner}:client:{request.ClientHash}", ct);
        if (await (await rows.GetQueryableAsync()).CountAsync(x => x.TenantId == owner && x.ClientHash == request.ClientHash && x.CreationTime > now.AddMinutes(-15), ct) >= 8) return false;
        await rows.InsertAsync(request, true, ct); await unit.CompleteAsync(ct); return true;
    }
    public async Task<RecoveryRequest?> FindAsync(string ticketHash, CancellationToken ct = default)
    {
        var owner = RecoveryStoreScope.Owner(tenant);
        using var unit = units.Begin(requiresNew: true, isTransactional: false);
        return await (await rows.GetQueryableAsync()).AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == owner && x.TicketHash == ticketHash, ct);
    }
    private async Task<T> Change<T>(Guid id, Func<RecoveryRequest, Task<T>> change, CancellationToken ct)
    {
        var owner = RecoveryStoreScope.Owner(tenant);
        using var unit = units.Begin(requiresNew: true, isTransactional: true);
        await Lock($"Recovery:{owner}:request:{id}", ct);
        var row = await rows.GetAsync(id, cancellationToken: ct);
        if (row.TenantId != owner) throw new InvalidOperationException("Recovery scope changed.");
        var result = await change(row); await rows.UpdateAsync(row, true, ct); await unit.CompleteAsync(ct); return result;
    }
    private async Task LockAccount(Guid owner, int studentUid, Guid directoryObjectId, CancellationToken ct)
    {
        // Serialize all tickets for either the same SIS student or the same directory account.
        await Lock($"Recovery:{owner}:student:{studentUid}", ct);
        await Lock($"Recovery:{owner}:directory:{directoryObjectId}", ct);
    }
    private async Task<IQueryable<RecoveryRequest>> AccountRequests(Guid owner, int studentUid, Guid directoryObjectId)
        => (await rows.GetQueryableAsync()).Where(x => x.TenantId == owner &&
            (x.StudentUid == studentUid || x.DirectoryObjectId == directoryObjectId));
    private static Task<bool> AccountBlocked(IQueryable<RecoveryRequest> account, RecoveryRequest row, CancellationToken ct)
        => account.AnyAsync(x => x.Id != row.Id && (x.State == "Applying" || x.State == "Unknown" ||
            x.State == "Complete" && (!x.PasswordWriteFinishedAtUtc.HasValue || x.PasswordWriteFinishedAtUtc >= row.CreationTime)), ct);
    public Task<RecoveryBindResult> BindAsync(Guid id, RecoveryPerson person, RecoveryDirectoryAccount account, RecoveryQuestion question, DateTime now, CancellationToken ct = default)
        => Change(id, async row => {
            var owner = RecoveryStoreScope.Owner(tenant);
            await LockAccount(owner, person.StudentUid, account.ObjectId, ct);
            var existing = await AccountRequests(owner, person.StudentUid, account.ObjectId);
            if (await existing.AnyAsync(x => x.Id != row.Id && (x.State == "Applying" || x.State == "Unknown"), ct))
            { row.Reject(); return RecoveryBindResult.PendingReset; }
            if (await AccountBlocked(existing, row, ct))
            { row.Reject(); return RecoveryBindResult.Expired; }
            if (await existing.CountAsync(x => x.StudentUid == person.StudentUid && x.CreationTime > now.AddMinutes(-15), ct) >= 3)
            { row.Reject(); return RecoveryBindResult.RateLimited; }
            return row.Bind(person, account, question, now) ? RecoveryBindResult.Bound : RecoveryBindResult.Expired;
        }, ct);
    public Task<bool> BeginAnswerAsync(Guid id, DateTime now, CancellationToken ct = default) => Change(id, row => Task.FromResult(row.BeginAnswer(now)), ct);
    public async Task FinishAnswerAsync(Guid id, bool verified, CancellationToken ct = default) => await Change(id, row => { row.FinishAnswer(verified); return Task.FromResult(true); }, ct);
    public Task<bool> ClaimResetAsync(Guid id, DateTime now, CancellationToken ct = default)
        => Change(id, async row => {
            if (row.StudentUid is not int studentUid || row.DirectoryObjectId is not Guid accountId) return false;
            var owner = RecoveryStoreScope.Owner(tenant);
            await LockAccount(owner, studentUid, accountId, ct);
            if (await AccountBlocked(await AccountRequests(owner, studentUid, accountId), row, ct))
            { row.Reject(); return false; }
            return row.ClaimReset(now);
        }, ct);
    public async Task FinishAsync(Guid id, RecoveryPasswordWriteResult outcome, CancellationToken ct = default)
    {
        // ABP treats CancellationToken.None as a request-token fallback. Override
        // that ambient token too so a disconnected browser cannot cancel recording
        // the outcome of an already-claimed external write. Keep completion bounded.
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(ct);
        completion.CancelAfter(TimeSpan.FromSeconds(15));
        using var independent = cancellationTokens.Use(completion.Token);
        ct = completion.Token;
        await Change(id, async row => {
            if (row.StudentUid is not int studentUid || row.DirectoryObjectId is not Guid accountId || row.State != "Applying") return false;
            var owner = RecoveryStoreScope.Owner(tenant);
            await LockAccount(owner, studentUid, accountId, ct);
            row.Finish(outcome, Now);
            if (row.State == "Complete")
            {
                // A successful recovery consumes every previously issued ticket for this account.
                var others = await (await AccountRequests(owner, studentUid, accountId)).Where(x => x.Id != id &&
                    (x.State == "Started" || x.State == "Question" || x.State == "Checking" || x.State == "Verified")).ToListAsync(ct);
                foreach (var other in others) { other.Reject(); await rows.UpdateAsync(other, false, ct); }
            }
            return true;
        }, completion.Token);
    }
    public async Task RejectAsync(Guid id, CancellationToken ct = default) => await Change(id, row => { row.Reject(); return Task.FromResult(true); }, ct);
}
