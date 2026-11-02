using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Web.Portals;

/// <summary>Holds an advisor's unsaved review only while Microsoft mail consent is in progress.</summary>
public sealed class AdvisorReviewMailDraftStore(IDistributedCache cache) : ITransientDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<Guid> SaveAsync(Guid userId, Guid? tenantId, SubmitAdvisorReviewInput input,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var draft = new PendingDraft(userId, tenantId, DateTimeOffset.UtcNow.Add(Lifetime), input);
        await cache.SetStringAsync(Key(id), JsonSerializer.Serialize(draft),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime },
            cancellationToken);
        return id;
    }

    public async Task<SubmitAdvisorReviewInput?> TakeAsync(Guid id, Guid userId, Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var json = await cache.GetStringAsync(Key(id), cancellationToken);
        if (json is null) return null;
        PendingDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<PendingDraft>(json);
        }
        catch (JsonException)
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
            return null;
        }
        if (draft is null || draft.UserId != userId || draft.TenantId != tenantId) return null;
        await cache.RemoveAsync(Key(id), cancellationToken);
        return draft.ExpiresAt > DateTimeOffset.UtcNow ? draft.Input : null;
    }

    private static string Key(Guid id) => $"CampusFlow:AdvisorReviewMailDraft:{id:N}";

    private sealed record PendingDraft(Guid UserId, Guid? TenantId, DateTimeOffset ExpiresAt,
        SubmitAdvisorReviewInput Input);
}
