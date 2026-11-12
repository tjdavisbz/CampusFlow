using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using CampusFlow.StudentInformationSystems;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace CampusFlow.Housing;

public sealed class MealPlanRemovalCoordinator(IRepository<MealPlanRemovalOperation, Guid> operations,
    IMealPlanRemovalGateway gateway, IStudentInformationSystemMealPlanRemovalService assignments,
    IClearanceResetProtection protection, IUnitOfWorkManager units, ICurrentTenant tenant,
    ICurrentUser user, IGuidGenerator ids) : IMealPlanRemovalCoordinator, ITransientDependency
{
    private Guid Owner() => tenant.Id is Guid id && string.Equals(tenant.Name, "nelson", StringComparison.OrdinalIgnoreCase)
        ? id : throw new UserFriendlyException("Select the university before reviewing meal-plan removal.");
    public Task<IAsyncDisposable> LockAsync(int studentUid, int termId) { Owner(); return protection.AcquireAsync(studentUid, termId); }
    public async Task<List<MealPlanRemovalTarget>> PendingAsync(int studentUid, int termId)
    {
        var owner = Owner();
        using var read = units.Begin(requiresNew: true, isTransactional: false);
        return (await operations.GetListAsync(x => x.TenantId == owner && x.StudentUid == studentUid && x.TermId == termId && x.State != "Completed"))
            .Select(x => JsonSerializer.Deserialize<MealPlanRemovalTarget>(x.TargetJson)!).ToList();
    }
    private async Task<MealPlanRemovalOperation?> Existing(MealPlanRemovalTarget target)
    {
        var owner = Owner();
        using var read = units.Begin(requiresNew: true, isTransactional: false);
        return await operations.FindAsync(x => x.TenantId == owner && x.AssignmentId == target.AssignmentId);
    }
    public async Task<MealPlanRemovalReview> ReviewAsync(MealPlanRemovalTarget target)
    {
        var owner = Owner(); var operation = await Existing(target);
        if (operation is not null && operation.TargetJson != JsonSerializer.Serialize(target))
            throw new UserFriendlyException("The saved removal identity changed. Business Services must review it.");
        if (operation?.State == "Completed") throw new UserFriendlyException("This removal is already complete. Refresh the page.");
        var live = await assignments.GetAssignmentsAsync(target.StudentUid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var sameTerm = live.Where(x => x.TermCalendarId == target.TermId).ToList();
        if (sameTerm.Any(x => x.AssignmentId != target.AssignmentId) ||
            (operation is null && sameTerm.Count != 1) ||
            sameTerm.Any(x => x.MealPlanId != target.MealPlanId || x.Amount != target.Amount || x.Name != target.Name))
            throw new UserFriendlyException("The term has a different or additional meal assignment. Review it before removing charges.");
        List<MealPlanRemovalEntry> entries;
        try { entries = await gateway.ReviewAsync(target); }
        catch (InvalidOperationException error) { throw new UserFriendlyException(error.Message); }
        if (operation is not null)
        {
            var reviewed = JsonSerializer.Deserialize<List<MealPlanRemovalEntry>>(operation.EntriesJson)!;
            if (entries.Any(x => !reviewed.Contains(x)))
                throw new UserFriendlyException("Billing changed after removal started. New or changed charges will not be deleted; Business Services must review them.");
        }
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { Policy = "reviewed-meal-removal-v1", Tenant = owner, User = user.Id, Target = target, Entries = entries.OrderBy(x => x.Id), Operation = operation?.Id, operation?.State }))));
        return new(target, entries, revision);
    }
    public async Task RemoveAsync(MealPlanRemovalTarget target, string revision, string reason)
    {
        if (user.Id is null || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 500)
            throw new UserFriendlyException("Review removal and enter a reason of 10–500 characters.");
        var owner = Owner(); await using var lease = await LockAsync(target.StudentUid, target.TermId);
        var review = await ReviewAsync(target);
        if (review.Revision != revision) throw new UserFriendlyException("The removal preview changed. Review the current assignment and charges again.");
        var operation = await Existing(target);
        if (operation is null)
        {
            operation = new(ids.Create(), owner, target, JsonSerializer.Serialize(review.Entries), reason);
            using var claim = units.Begin(requiresNew: true, isTransactional: true);
            await operations.InsertAsync(operation, autoSave: true); await claim.CompleteAsync();
        }
        var operationId = operation.Id;
        async Task Record(string stage, bool complete = false, bool uncertain = false)
        {
            using var change = units.Begin(requiresNew: true, isTransactional: true);
            var row = await operations.GetAsync(operationId);
            if (row.TenantId != owner) throw new InvalidOperationException("Removal tenant changed.");
            row.Record(stage); if (complete) row.Complete(); if (uncertain) row.NeedsReview();
            await operations.UpdateAsync(row, autoSave: true); await change.CompleteAsync();
        }
        try
        {
            var live = await assignments.GetAssignmentsAsync(target.StudentUid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (live.Any(x => x.AssignmentId == target.AssignmentId))
            {
                await Record("Starting exact meal assignment removal");
                await assignments.RemoveAsync(target.StudentUid.ToString(System.Globalization.CultureInfo.InvariantCulture), target.TermId, target.AssignmentId, target.MealPlanId);
            }
            if ((await assignments.GetAssignmentsAsync(target.StudentUid.ToString(System.Globalization.CultureInfo.InvariantCulture))).Any(x => x.TermCalendarId == target.TermId))
                throw new InvalidOperationException("The meal assignment removal is not confirmed.");
            foreach (var entry in review.Entries)
            {
                await Record($"Starting removal of reviewed {entry.Kind} batch entry {entry.Id}");
                await gateway.DeleteAsync(target, entry);
                await Record($"Removal verified for batch entry {entry.Id}");
            }
            if ((await gateway.ReviewAsync(target)).Count != 0) throw new InvalidOperationException("Meal charges remain after removal.");
            await Record("Removal verified", complete: true);
        }
        catch (Exception)
        {
            await Record("Removal needs review; previous snapshots retained", uncertain: true);
            throw new UserFriendlyException("Meal-plan removal did not finish. A saved removal review is available on this page. Review it before continuing; selection and bill approval remain blocked until cleanup is confirmed.");
        }
    }
}
