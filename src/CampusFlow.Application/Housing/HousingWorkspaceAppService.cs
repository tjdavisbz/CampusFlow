using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CampusFlow.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
public class HousingWorkspaceAppService : CampusFlowAppService, IHousingWorkspaceAppService
{
    private readonly IRepository<HousingAssignmentDraft, Guid> _drafts;
    private readonly IRepository<HousingRateSchedule, Guid> _rateSchedules;
    private readonly IElementsHousingReader _elements;
    private readonly IElementsHousingAssignmentWriter _writer;
    private readonly IElementsHousingBillingWriter _billingWriter;
    private readonly IConfiguration _configuration;
    private readonly IStudentInformationSystemStudentLookup _students;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    public HousingWorkspaceAppService(IRepository<HousingAssignmentDraft, Guid> drafts,
        IRepository<HousingRateSchedule, Guid> rateSchedules, IElementsHousingReader elements,
        IElementsHousingAssignmentWriter writer, IElementsHousingBillingWriter billingWriter,
        IConfiguration configuration, IUnitOfWorkManager unitOfWorkManager,
        IEnumerable<IStudentInformationSystemStudentLookup> students)
    {
        _drafts = drafts; _rateSchedules = rateSchedules; _elements = elements; _writer = writer; _billingWriter = billingWriter;
        _configuration = configuration;
        _unitOfWorkManager = unitOfWorkManager;
        _students = students.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
    }
    public async Task<HousingPeriodSetup> GetPeriodSetupAsync(int periodId)
    {
        Tenant();
        if (periodId <= 0) throw new UserFriendlyException("Choose a housing period.");
        try { return await _elements.GetPeriodSetupAsync(periodId); }
        catch (InvalidOperationException exception) { throw new UserFriendlyException(exception.Message); }
    }
    private Guid Tenant() => CurrentTenant.Id ?? throw new UserFriendlyException("Select a university before managing housing.");
    private async Task<HousingAssignmentDraft?> Find(int periodId)
    {
        var tenant = Tenant();
        return await _drafts.FindAsync(x => x.TenantId == tenant && x.PeriodId == periodId);
    }
    private async Task<(List<HousingRateAmount> Rates, string Revision)> SavedRatesAsync(int periodId, int termCalendarId)
    {
        var tenant = Tenant();
        var schedule = await _rateSchedules.FindAsync(x => x.TenantId == tenant && x.PeriodId == periodId);
        if (schedule is null || schedule.TermCalendarId != termCalendarId)
            throw new UserFriendlyException("Save the housing fee amounts in Admin → Housing → Fee Setup before applying charges.");
        var rates = JsonSerializer.Deserialize<List<HousingRateAmount>>(schedule.RatesJson)
            ?? throw new UserFriendlyException("The saved housing fee amounts could not be read.");
        return (rates, schedule.ConcurrencyStamp);
    }
    public async Task<string> GetRateRevisionAsync(int periodId)
    {
        var draft=await Find(periodId)??throw new UserFriendlyException("Refresh from Elements first.");
        if(!NeedsResidentialRates(Read(draft.BaselineJson),Read(draft.RoomsJson)))return "";
        var pricing = await _elements.GetPricingAsync(periodId);
        return (await SavedRatesAsync(periodId, pricing.TermCalendarId)).Revision;
    }
    public async Task<List<HousingPeriodOption>> GetPeriodsAsync() { Tenant(); return await _elements.GetPeriodsAsync(); }
    public async Task<HousingWorkspaceState?> GetAsync(int periodId)
    {
        var draft = await Find(periodId);
        return draft is null ? null : Map(draft);
    }
    public async Task<HousingWorkspaceState> RefreshAsync(int periodId, string? revision)
    {
        var tenant = Tenant();
        if (!(await _elements.GetPeriodsAsync()).Any(x => x.Id == periodId))
            throw new UserFriendlyException("Choose an available housing period.");
        var draft = await Find(periodId);
        if (draft is not null) { EnsureEditable(draft,refreshing:true);CheckRevision(draft, revision); }
        else if (!string.IsNullOrEmpty(revision)) throw new UserFriendlyException("This draft no longer exists. Reload the page.");
        var rooms = await _elements.GetRoomsAsync(periodId);
        // Elements may already contain over-capacity rooms. Import the real snapshot so
        // staff can see and resolve them; only Apply enforces capacity compliance.
        Validate(rooms, rooms, requireCapacityCompliance: false);
        if (draft is not null)
        {
            // Rate choices and intentional-single flags are CampusFlow billing metadata,
            // not Elements room fields. Preserve only values from the last successful
            // Apply; discard any newer, unapplied draft edits during Refresh.
            var applied = Read(draft.BaselineJson)
                .SelectMany(room => room.Occupants.Select(occupant => (room.Id, occupant)))
                .ToDictionary(x => (x.Id, x.occupant.StudentUid));
            foreach (var room in rooms)
                foreach (var occupant in room.Occupants)
                    if (applied.TryGetValue((room.Id, occupant.StudentUid), out var previous))
                    {
                        occupant.Rate = previous.occupant.Rate;
                        occupant.IntentionalSingle = previous.occupant.IntentionalSingle;
                        occupant.Notes = previous.occupant.Notes;
                    }
        }
        var json = JsonSerializer.Serialize(rooms);
        if (draft is null)
        {
            draft = new HousingAssignmentDraft(GuidGenerator.Create(), tenant, periodId, json, Clock.Now);
            await _drafts.InsertAsync(draft, autoSave: true);
        }
        else { draft.Refresh(json, Clock.Now);var progress=HousingApplyProgress.Read(draft);if(progress is not null){if(progress.Status!="Completed"){progress.Status="Reviewed";progress.FinishedAt=Clock.Now;}progress.Save(draft);}await _drafts.UpdateAsync(draft, autoSave: true); }
        return Map(draft);
    }
    public async Task<HousingWorkspaceState> SaveAsync(HousingWorkspaceState input)
    {
        var draft = await Find(input.PeriodId) ?? throw new UserFriendlyException("Refresh from Elements first.");
        EnsureEditable(draft);
        if (draft.Id != input.Id) throw new UserFriendlyException("Reload the current draft.");
        CheckRevision(draft, input.Revision);
        var baseline = Read(draft.BaselineJson);
        Validate(baseline, input.Rooms, requireCapacityCompliance: false);
        var known = Read(draft.RoomsJson).SelectMany(x => x.Occupants).ToDictionary(x => x.StudentUid, x => x.Name);
        foreach (var room in input.Rooms)
        {
            var original = baseline.Single(x => x.Id == room.Id);
            room.Name = original.Name; room.Building = original.Building;
            foreach (var occupant in room.Occupants)
            {
                if (!known.TryGetValue(occupant.StudentUid, out var name))
                {
                    var student = await _students.FindByExternalStudentIdAsync(occupant.StudentUid.ToString())
                        ?? throw new UserFriendlyException("A selected student no longer exists.");
                    name = $"{student.FirstName} {student.LastName}";
                }
                occupant.Name = name;
            }
        }
        draft.Save(JsonSerializer.Serialize(input.Rooms));
        await _drafts.UpdateAsync(draft, autoSave: true);
        return Map(draft);
    }
    public async Task<List<HousingWorkspaceChange>> PreviewAsync(int periodId)
    {
        var draft = await Find(periodId) ?? throw new UserFriendlyException("Refresh from Elements first.");
        var before = Read(draft.BaselineJson);
        var after = Read(draft.RoomsJson);
        var changes = Validate(before, after, requireCapacityCompliance: false);
        if (!_configuration.GetValue<bool>("Housing:EnableNonProductionWrites") &&
            !_configuration.GetValue<bool>("Housing:EnableProductionWrites")) return changes;
        if(!changes.Any(x=>x.Action is "Assign" or "Move" or "Remove" or "Rate review"))return changes;
        var pricing = await _elements.GetPricingAsync(periodId);
        var configuredRates = NeedsResidentialRates(before,after)
            ? (await SavedRatesAsync(periodId, pricing.TermCalendarId)).Rates : new List<HousingRateAmount>();
        var oldAssignments = before.SelectMany(room => room.Occupants.Select(o => (room, o)))
            .ToDictionary(x => x.o.StudentUid);
        var newAssignments = after.SelectMany(room => room.Occupants.Select(o => (room, o)))
            .ToDictionary(x => x.o.StudentUid);
        foreach (var (studentId, proposed) in newAssignments)
        {
            if (oldAssignments.TryGetValue(studentId, out var old) && old.room.Id == proposed.room.Id &&
                old.o.Rate == proposed.o.Rate && old.o.IntentionalSingle == proposed.o.IntentionalSingle) continue;
            IReadOnlyList<HousingChargeLine> lines;
            try
            {
                lines = HousingChargeCalculator.Quote(pricing.TermCalendarId, proposed.room.Building,
                    proposed.o.Rate, proposed.o.IntentionalSingle, pricing.Fees, configuredRates,
                    pricing.SingleTransactionDocumentId, pricing.SingleAccountsReceivableTypeId);
            }
            catch (ArgumentException e) { throw new UserFriendlyException(e.Message); }
            foreach (var line in lines)
                changes.Add(new HousingWorkspaceChange { Action = "Proposed charge",
                    Detail = $"{proposed.o.Name}: {line.Description} — {line.Amount:C2} (document {line.TransactionDocumentId})" });
        }
        foreach (var (studentId, old) in oldAssignments)
            if (!newAssignments.ContainsKey(studentId))
                changes.Add(new HousingWorkspaceChange { Action = "Proposed charge removal",
                    Detail = $"{old.o.Name}: CampusFlow-managed housing batch charges would be removed; existing manual or posted charges will not be changed." });
        return changes;
    }
    [UnitOfWork(IsDisabled = true)]
    public async Task<HousingWorkspaceState> ApplyAssignmentsAsync(ApplyHousingDraftInput input)
    {
        using var preparation=_unitOfWorkManager.Begin(requiresNew:true,isTransactional:false);
        if (!input.Confirmed) throw new UserFriendlyException("Review and confirm the Elements changes before applying them.");
        _writer.EnsureWriteEnabled();
        var draft = await Find(input.PeriodId) ?? throw new UserFriendlyException("Refresh from Elements first.");
        CheckRevision(draft, input.Revision);
        EnsureEditable(draft);
        var before = Read(draft.BaselineJson);
        var after = Read(draft.RoomsJson);
        Validate(before, after);

        var capacityChanges=after.Where(room=>room.Capacity!=before.Single(x=>x.Id==room.Id).Capacity)
            .Select(room=>new HousingCapacityChange(room.Id,before.Single(x=>x.Id==room.Id).Capacity,room.Capacity)).ToList();
        // Notes stay in CampusFlow and do not prevent room/charge changes.
        var oldAssignments = before.SelectMany(room => room.Occupants.Select(o => (room, o)))
            .ToDictionary(x => x.o.StudentUid);
        var newAssignments = after.SelectMany(room => room.Occupants.Select(o => (room, o)))
            .ToDictionary(x => x.o.StudentUid);
        var roomChangedIds = oldAssignments.Keys.Union(newAssignments.Keys)
            .Where(id => !oldAssignments.TryGetValue(id, out var old) ||
                         !newAssignments.TryGetValue(id, out var desired) || old.room.Id != desired.room.Id)
            .Order().ToList();
        var changedIds = oldAssignments.Keys.Union(newAssignments.Keys)
            .Where(id => roomChangedIds.Contains(id) ||
                         oldAssignments[id].o.Rate != newAssignments[id].o.Rate ||
                         oldAssignments[id].o.IntentionalSingle != newAssignments[id].o.IntentionalSingle)
            .Order().ToList();
        if (changedIds.Count == 0 && capacityChanges.Count == 0) return Map(draft);

        var pricing = changedIds.Count>0 ? await _elements.GetPricingAsync(input.PeriodId) : new HousingPricingContext(0,Array.Empty<HousingTermFee>(),0);
        var needsRates=NeedsResidentialRates(before,after);
        var savedRates = needsRates ? await SavedRatesAsync(input.PeriodId, pricing.TermCalendarId) : (Rates:new List<HousingRateAmount>(),Revision:"");
        if (savedRates.Revision != input.RateRevision)
            throw new UserFriendlyException("Housing fee amounts changed since the preview. Review charges again before applying.");
        var configuredRates = savedRates.Rates;
        var documents = pricing.Fees.Select(x=>x.TransactionDocumentId).Append(pricing.SingleTransactionDocumentId).Where(x=>x>0).Distinct().ToArray();
        var proposedCharges = new Dictionary<int, IReadOnlyList<HousingChargeLine>>();
        foreach (var id in changedIds)
        {
            if (newAssignments.TryGetValue(id, out var proposed))
            {
                try
                {
                    proposedCharges[id] = HousingChargeCalculator.Quote(pricing.TermCalendarId,
                        proposed.room.Building, proposed.o.Rate, proposed.o.IntentionalSingle,
                        pricing.Fees, configuredRates, pricing.SingleTransactionDocumentId,
                        pricing.SingleAccountsReceivableTypeId);
                }
                catch (ArgumentException e) { throw new UserFriendlyException(e.Message); }
            }
            else proposedCharges[id] = [];
            await _billingWriter.VerifyCanReconcileAsync(id, pricing.TermCalendarId, documents);
        }

        var current = await _elements.GetRoomsAsync(input.PeriodId);
        if (!SameAssignments(before, current))
            throw new UserFriendlyException("Elements assignments or room capacities changed since this draft was refreshed. Review and refresh before applying changes.");

        // The replica is only a first check. Confirm every affected student through the live API.
        foreach (var id in changedIds)
        {
            var actual = await _writer.GetAssignedRoomAsync(id, input.PeriodId);
            if (!oldAssignments.TryGetValue(id, out var old))
            {
                if (actual is not null)
                    throw new UserFriendlyException("A student has a new Elements assignment. Refresh the draft before applying changes.");
            }
            else if (actual is null || !SameRoom(actual.Value, old.room))
                throw new UserFriendlyException("A student's Elements room differs from the saved baseline. Refresh the draft before applying changes.");
        }

        var touchedRoomIds=roomChangedIds.SelectMany(id=>new[]{oldAssignments.TryGetValue(id,out var old)?old.room.Id:0,newAssignments.TryGetValue(id,out var desired)?desired.room.Id:0})
            .Concat(capacityChanges.Select(x=>x.RoomId)).Where(x=>x>0).ToHashSet();
        await _writer.VerifyRoomsAsync(input.PeriodId,before.Where(x=>touchedRoomIds.Contains(x.Id)).ToList());
        await preparation.CompleteAsync();
        var run=Guid.NewGuid();
        using(var claim=_unitOfWorkManager.Begin(requiresNew:true,isTransactional:true))
        {
            var latest=await Find(input.PeriodId)??throw new UserFriendlyException("The draft no longer exists.");
            CheckRevision(latest,input.Revision);EnsureEditable(latest);
            if(latest.RoomsJson!=draft.RoomsJson||latest.BaselineJson!=draft.BaselineJson ||
                (needsRates && (await SavedRatesAsync(input.PeriodId,pricing.TermCalendarId)).Revision!=input.RateRevision))
                throw new UserFriendlyException("The housing draft or fees changed. Review again.");
            new HousingApplyProgress{RunId=run,StartedAt=Clock.Now,UserId=CurrentUser.Id,BeforeJson=latest.BaselineJson,DesiredJson=latest.RoomsJson}.Save(latest);
            await _drafts.UpdateAsync(latest,autoSave:true);await claim.CompleteAsync();
        }
        async Task Record(string operation)
        {
            using var unit=_unitOfWorkManager.Begin(requiresNew:true,isTransactional:true);
            var latest=await Find(input.PeriodId)??throw new InvalidOperationException("The apply draft disappeared.");
            var progress=HousingApplyProgress.Read(latest);
            if(progress?.RunId!=run||progress.Status!="Running")throw new InvalidOperationException("Housing apply ownership changed.");
            progress.Operations.Add(operation);progress.UpdatedAt=Clock.Now;progress.Save(latest);await _drafts.UpdateAsync(latest,autoSave:true);await unit.CompleteAsync();
        }
        try
        {
            // Expand capacities before assignment; shrink only after students have been removed.
            var increases=capacityChanges.Where(x=>x.Capacity>x.PreviousCapacity).ToList();
            if(increases.Count>0){await Record("Starting capacity increases");await _writer.UpdateCapacitiesAsync(input.PeriodId,increases);await Record("Capacity increases verified");}
            foreach(var id in roomChangedIds.Where(oldAssignments.ContainsKey))
            {
                await Record($"Starting room removal for {id}");await _writer.DeallocateAsync(id,input.PeriodId,oldAssignments[id].room.Id);
                if(await _writer.GetAssignedRoomAsync(id,input.PeriodId) is not null)throw new InvalidOperationException("Room removal was not confirmed.");
                await Record($"Room removal verified for {id}");
            }
            var decreases=capacityChanges.Where(x=>x.Capacity<x.PreviousCapacity).ToList();
            if(decreases.Count>0){await Record("Starting capacity decreases");await _writer.UpdateCapacitiesAsync(input.PeriodId,decreases);await Record("Capacity decreases verified");}
            foreach(var id in roomChangedIds.Where(newAssignments.ContainsKey))
            {
                await Record($"Starting room assignment for {id}");var room=newAssignments[id].room;await _writer.AssignAsync(id,input.PeriodId,room.Id);
                var confirmed=await _writer.GetAssignedRoomAsync(id,input.PeriodId);
                if(confirmed is null||!SameRoom(confirmed.Value,room))throw new InvalidOperationException("New room assignment was not confirmed.");
                await Record($"Room assignment verified for {id}");
            }
            foreach(var id in changedIds)
            {
                await Record($"Starting charge reconciliation for {id}");
                await _billingWriter.ReconcileAsync(id,pricing.TermCalendarId,proposedCharges[id],documents);await Record($"Charges verified for {id}");
            }
            using var finish=_unitOfWorkManager.Begin(requiresNew:true,isTransactional:true);
            var latest=await Find(input.PeriodId)??throw new InvalidOperationException("The draft disappeared.");
            var progress=HousingApplyProgress.Read(latest)??throw new InvalidOperationException("Apply history is missing.");
            if(progress.RunId!=run)throw new InvalidOperationException("Housing apply ownership changed.");
            progress.Status="Completed";progress.FinishedAt=Clock.Now;progress.Save(latest);
            latest.Refresh(JsonSerializer.Serialize(after),Clock.Now);await _drafts.UpdateAsync(latest,autoSave:true);await finish.CompleteAsync();return Map(latest);
        }
        catch(Exception)
        {
            using var failed=_unitOfWorkManager.Begin(requiresNew:true,isTransactional:true);
            var latest=await Find(input.PeriodId);var progress=latest is null?null:HousingApplyProgress.Read(latest);
            if(progress?.RunId==run){progress.Status="Needs review";progress.FinishedAt=Clock.Now;progress.Save(latest!);await _drafts.UpdateAsync(latest!,autoSave:true);await failed.CompleteAsync();}
            throw new UserFriendlyException("The apply did not finish. Review the live Elements assignments, room capacities and housing batch before refreshing. Further apply attempts are blocked until that review and refresh.");
        }
    }
    public async Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId)
    {
        Tenant();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];
        return await _elements.SearchStudentsAsync(query.Trim(), periodId);
    }
    public async Task<HousingTermCopyPreview> PreviewTermCopyAsync(int sourcePeriodId, int destinationPeriodId)
    {
        var (preview, _, _, _, _) = await PrepareTermCopyAsync(sourcePeriodId, destinationPeriodId);
        return preview;
    }

    [UnitOfWork(IsDisabled = true)]
    public async Task<HousingWorkspaceState> CopyTermDraftAsync(CopyHousingTermInput input)
    {
        if (!input.Confirmed) throw new UserFriendlyException("Confirm the reviewed term copy before creating the draft.");
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var (preview, destination, copied, sourceRates, destinationTermId) =
            await PrepareTermCopyAsync(input.SourcePeriodId, input.DestinationPeriodId);
        if (input.ExpectedStudents != preview.StudentsToCopy ||
            input.ExpectedFeeRevision != preview.FeeRevision ||
            input.ExpectedCopyRevision != preview.CopyRevision)
            throw new UserFriendlyException("The source assignments or fee amounts changed. Preview the term copy again.");
        var draft = new HousingAssignmentDraft(GuidGenerator.Create(), Tenant(), input.DestinationPeriodId,
            JsonSerializer.Serialize(destination), Clock.Now);
        draft.Save(JsonSerializer.Serialize(copied));
        await _drafts.InsertAsync(draft, autoSave: true);
        await _rateSchedules.InsertAsync(new HousingRateSchedule(GuidGenerator.Create(), Tenant(),
            input.DestinationPeriodId, destinationTermId, sourceRates.RatesJson), autoSave: true);
        await uow.CompleteAsync();
        return Map(draft);
    }

    private async Task<(HousingTermCopyPreview Preview, List<HousingWorkspaceRoom> Destination,
        List<HousingWorkspaceRoom> Copied, HousingRateSchedule SourceRates, int DestinationTermId)>
        PrepareTermCopyAsync(int sourcePeriodId, int destinationPeriodId)
    {
        Tenant();
        if (sourcePeriodId <= 0 || destinationPeriodId <= 0 || sourcePeriodId == destinationPeriodId)
            throw new UserFriendlyException("Choose two different housing periods.");
        var periods = await _elements.GetPeriodsAsync();
        var sourcePeriod = periods.SingleOrDefault(x => x.Id == sourcePeriodId);
        var destinationPeriod = periods.SingleOrDefault(x => x.Id == destinationPeriodId);
        if (sourcePeriod is null || destinationPeriod is null)
            throw new UserFriendlyException("One of the housing periods is no longer available.");
        if (await Find(destinationPeriodId) is not null)
            throw new UserFriendlyException("The destination already has a CampusFlow draft. A term can be copied only once; review the existing draft instead.");
        var sourceRates = await _rateSchedules.FindAsync(x => x.TenantId == CurrentTenant.Id && x.PeriodId == sourcePeriodId)
            ?? throw new UserFriendlyException("Save the source term's fee amounts before copying it.");
        if (await _rateSchedules.FindAsync(x => x.TenantId == CurrentTenant.Id && x.PeriodId == destinationPeriodId) is not null)
            throw new UserFriendlyException("The destination already has fee amounts. Review them instead of copying over them.");
        HousingPricingContext sourcePricing, destinationPricing;
        try
        {
            sourcePricing = await _elements.GetPricingAsync(sourcePeriodId);
            destinationPricing = await _elements.GetPricingAsync(destinationPeriodId);
        }
        catch (InvalidOperationException exception) { throw new UserFriendlyException(exception.Message); }
        if (sourceRates.TermCalendarId != sourcePricing.TermCalendarId)
            throw new UserFriendlyException("The source fee amounts are for a different term.");
        var sourceAmounts = JsonSerializer.Deserialize<List<HousingRateAmount>>(sourceRates.RatesJson) ?? [];
        IReadOnlyList<HousingRateDefinition> destinationDefinitions;
        try { destinationDefinitions = HousingChargeCalculator.GetRateDefinitions(destinationPricing.TermCalendarId); }
        catch (ArgumentException exception) { throw new UserFriendlyException(exception.Message); }
        var sourceKeys = sourceAmounts.Select(x => (x.Building, x.RateChoice)).ToHashSet();
        var destinationKeys = destinationDefinitions.Select(x => (x.Building, x.RateChoice)).ToHashSet();
        if (sourceKeys.Count != sourceAmounts.Count || !sourceKeys.SetEquals(destinationKeys) ||
            sourceAmounts.Any(x => x.BaseAmount <= 0 || x.SingleSurchargeAmount <= 0))
            throw new UserFriendlyException("The destination term has different fee choices. Review its rate setup before copying.");
        var source = await _elements.GetRoomsAsync(sourcePeriodId);
        var destination = await _elements.GetRoomsAsync(destinationPeriodId);
        var sourceDraft = await Find(sourcePeriodId);
        List<HousingWorkspaceRoom> copied;
        try { copied = HousingTermCopyPlanner.Copy(source, destination,
                sourceDraft is null ? null : Read(sourceDraft.BaselineJson)); }
        catch (ArgumentException exception) { throw new UserFriendlyException(exception.Message); }
        var preview = new HousingTermCopyPreview
        {
            SourcePeriodId = sourcePeriodId, DestinationPeriodId = destinationPeriodId,
            SourcePeriodName = sourcePeriod.Name, DestinationPeriodName = destinationPeriod.Name,
            StudentsToCopy = copied.Sum(room => room.Occupants.Count), DestinationRooms = destination.Count,
            IntentionalSinglesToReview = copied.Sum(room => room.Occupants.Count(x => x.IntentionalSingle)),
            FeesToCopy = sourceAmounts.Select(x => new HousingTermCopyFeeAmount { Building = x.Building,
                RateChoice = x.RateChoice, BaseAmount = x.BaseAmount,
                SingleSurchargeAmount = x.SingleSurchargeAmount }).ToList(),
            FeeRevision = sourceRates.ConcurrencyStamp,
            CopyRevision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(copied)))),
            Warning = "This creates an assignment draft and copies fee amounts in CampusFlow only. It does not change Elements. Applied rate and intentional-single choices carry forward; unknown choices require review. Review all copied fees before applying charges."
        };
        return (preview, destination, copied, sourceRates, destinationPricing.TermCalendarId);
    }
    private static bool NeedsResidentialRates(List<HousingWorkspaceRoom> before,List<HousingWorkspaceRoom> after)
    {
        var old=before.SelectMany(r=>r.Occupants.Select(o=>(r,o))).ToDictionary(x=>x.o.StudentUid);
        return after.Where(r=>!StudentHousingEligibility.IsCommuterBuilding(r.Building)).Any(r=>r.Occupants.Any(o=>
            !old.TryGetValue(o.StudentUid,out var previous)||previous.r.Id!=r.Id||previous.o.Rate!=o.Rate||previous.o.IntentionalSingle!=o.IntentionalSingle));
    }
    private void EnsureEditable(HousingAssignmentDraft draft,bool refreshing=false)
    {
        try { HousingApplyProgress.EnsureEditable(draft,Clock.Now,refreshing); }
        catch(InvalidOperationException e) { throw new UserFriendlyException(e.Message); }
    }
    private static void CheckRevision(HousingAssignmentDraft draft, string? revision)
    {
        if (draft.ConcurrencyStamp != revision) throw new UserFriendlyException("Another user changed this draft. Reload before continuing.");
    }
    private static List<HousingWorkspaceChange> Validate(List<HousingWorkspaceRoom> before, List<HousingWorkspaceRoom> after,
        bool requireCapacityCompliance = true)
    {
        try { return HousingWorkspacePlanner.Compare(before, after, requireCapacityCompliance); }
        catch (ArgumentException e) { throw new UserFriendlyException(e.Message); }
    }
    private static List<HousingWorkspaceRoom> Read(string json) => JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(json)!;
    private static bool SameRoom((string Building, string Room) actual, HousingWorkspaceRoom expected) =>
        string.Equals(actual.Building.Trim(), expected.Building.Trim(), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(actual.Room.Trim(), expected.Name.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameAssignments(List<HousingWorkspaceRoom> before, List<HousingWorkspaceRoom> current) =>
        before.Count == current.Count && before.All(room =>
        {
            var found = current.SingleOrDefault(x => x.Id == room.Id);
            return found is not null && found.Capacity == room.Capacity &&
                   room.Occupants.Select(x => x.StudentUid).Order()
                       .SequenceEqual(found.Occupants.Select(x => x.StudentUid).Order());
        });
    private static HousingWorkspaceState Map(HousingAssignmentDraft x) => new()
    { Id = x.Id, PeriodId = x.PeriodId, Revision = x.ConcurrencyStamp, RefreshedAt = x.RefreshedAt, Rooms = Read(x.RoomsJson), ApplyStatus = HousingApplyProgress.Read(x)?.Status };
}
