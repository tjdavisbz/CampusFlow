using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp;

namespace CampusFlow.Housing;

public partial class HousingWorkspaceAppService
{
    private async Task<(HousingRecoveryPreview Preview, List<HousingWorkspaceRoom> Current, List<int> Remaining,
        List<HousingHistoricalAssignment> Historical, Dictionary<int,string> Billing)> PrepareBillingRecoveryAsync(
        HousingAssignmentDraft draft, HousingApplyProgress progress, List<HousingWorkspaceRoom> before,
        List<HousingWorkspaceRoom> desired, List<HousingWorkspaceRoom> current)
    {
        List<int> changed;
        try { changed = HousingBillingRecovery.Review(before, desired, current, BillingOrigin(draft,progress).Operations); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { throw new UserFriendlyException(error.Message); }
        var next = desired.SelectMany(r=>r.Occupants.Select(o=>(Room:r,Occupant:o))).ToDictionary(x=>x.Occupant.StudentUid);
        var old = before.SelectMany(r=>r.Occupants).ToDictionary(x=>x.StudentUid);
        var pricing = await _elements.GetPricingAsync(draft.PeriodId);
        var needsRates = NeedsResidentialRates(before,desired);
        var rates = needsRates ? await SavedRatesAsync(draft.PeriodId,pricing.TermCalendarId) : (Rates:new List<HousingRateAmount>(),Revision:"");
        var documents = HousingChargeCalculator.BillingDocumentIds(pricing);
        var reviews = new List<HousingBillingReview>();
        var lines = new List<HousingBillingRecoveryLine>();
        foreach (var id in changed)
        {
            var live = await _writer.GetEffectiveAssignedRoomAsync(id,draft.PeriodId);
            if (next.TryGetValue(id,out var target) ? live is null || !SameRoom(live.Value,target.Room) : live is not null)
                throw new UserFriendlyException("The live room assignments changed during billing review. No recovery was started.");
            IReadOnlyList<HousingChargeLine> wanted;
            try
            {
                wanted = next.TryGetValue(id,out target)
                    ? HousingChargeCalculator.Quote(pricing.TermCalendarId,target.Room.Building,target.Occupant.Rate,
                        target.Occupant.IntentionalSingle,pricing.Fees,rates.Rates,pricing.SingleTransactionDocumentId,pricing.SingleAccountsReceivableTypeId)
                    : [];
                var review = await _billingWriter.ReviewAsync(id,pricing.TermCalendarId,wanted,documents);
                if (review.StudentUid != id || review.TermCalendarId != pricing.TermCalendarId || string.IsNullOrWhiteSpace(review.Revision))
                    throw new InvalidOperationException("The housing billing review identity could not be verified.");
                reviews.Add(review);
                lines.Add(new(id,next.TryGetValue(id,out target)?target.Occupant.Name:old[id].Name,review.CurrentAmount,review.ProposedAmount));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            { throw new UserFriendlyException($"Housing billing review stopped. {error.Message} The saved plan is retained."); }
        }
        var identity = JsonSerializer.Serialize(new { Policy="billing-only-recovery-v1",Tenant=Tenant(),User=CurrentUser.Id,
            draft.Id,draft.PeriodId,progress.RunId,draft.BaselineJson,draft.RoomsJson,RateRevision=rates.Revision,
            Reviews=reviews.OrderBy(x=>x.StudentUid),Rooms=current.OrderBy(x=>x.Id).Select(r=>new { r.Id,r.Building,r.Name,r.Capacity,
                Students=r.Occupants.Select(x=>x.StudentUid).Order().ToArray() }) });
        return (new HousingRecoveryPreview { PeriodId=draft.PeriodId,RunId=progress.RunId,Revision=draft.ConcurrencyStamp,
            RecoveryRevision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),Mode="billing",
            RateRevision=rates.Revision,SavedAssignments=desired.Sum(r=>r.Occupants.Count),AlreadyAssigned=changed.Count,
            RemainingAssignments=0,BillingChanges=lines,Failure=HousingApplyFailure.Description(progress.FailureCode) },
            current,[],[],reviews.ToDictionary(x=>x.StudentUid,x=>x.Revision));
    }

    private static HousingApplyProgress BillingOrigin(HousingAssignmentDraft draft,HousingApplyProgress progress)
    {
        var history = draft.ExtraProperties.TryGetValue("HousingApplyHistoryV1",out var raw) && raw is not null
            ? JsonSerializer.Deserialize<List<HousingApplyProgress>>(raw.ToString()!) ?? [] : [];
        var seen = new HashSet<Guid>();
        while (progress.RecoveryMode == "billing")
        {
            if (!seen.Add(progress.RunId) || progress.RecoveryOfRunId is not Guid previous)
                throw new InvalidOperationException("The original billing recovery history could not be verified.");
            var matches = history.Where(x=>x.RunId==previous).ToList();
            if (matches.Count!=1 || matches[0].BeforeJson!=draft.BaselineJson || matches[0].DesiredJson!=draft.RoomsJson)
                throw new InvalidOperationException("The original billing recovery plan changed. Review the saved history.");
            progress=matches[0];
        }
        return progress;
    }
}
