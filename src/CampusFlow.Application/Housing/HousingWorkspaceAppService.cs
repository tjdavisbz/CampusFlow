using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

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
    public HousingWorkspaceAppService(IRepository<HousingAssignmentDraft, Guid> drafts,
        IRepository<HousingRateSchedule, Guid> rateSchedules, IElementsHousingReader elements,
        IElementsHousingAssignmentWriter writer, IElementsHousingBillingWriter billingWriter,
        IConfiguration configuration,
        IEnumerable<IStudentInformationSystemStudentLookup> students)
    {
        _drafts = drafts; _rateSchedules = rateSchedules; _elements = elements; _writer = writer; _billingWriter = billingWriter;
        _configuration = configuration;
        _students = students.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
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
        if (draft is not null) CheckRevision(draft, revision);
        else if (!string.IsNullOrEmpty(revision)) throw new UserFriendlyException("This draft no longer exists. Reload the page.");
        var rooms = await _elements.GetRoomsAsync(periodId);
        Validate(rooms, rooms);
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
                    }
        }
        var json = JsonSerializer.Serialize(rooms);
        if (draft is null)
        {
            draft = new HousingAssignmentDraft(GuidGenerator.Create(), tenant, periodId, json, Clock.Now);
            await _drafts.InsertAsync(draft, autoSave: true);
        }
        else { draft.Refresh(json, Clock.Now); await _drafts.UpdateAsync(draft, autoSave: true); }
        return Map(draft);
    }
    public async Task<HousingWorkspaceState> SaveAsync(HousingWorkspaceState input)
    {
        var draft = await Find(input.PeriodId) ?? throw new UserFriendlyException("Refresh from Elements first.");
        if (draft.Id != input.Id) throw new UserFriendlyException("Reload the current draft.");
        CheckRevision(draft, input.Revision);
        var baseline = Read(draft.BaselineJson);
        Validate(baseline, input.Rooms);
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
        var changes = Validate(before, after);
        if (!string.Equals(_configuration["App:EnvironmentLabel"], "NON-PRODUCTION",
                StringComparison.OrdinalIgnoreCase)) return changes;
        var pricing = await _elements.GetPricingAsync(periodId);
        var configuredRates = (await SavedRatesAsync(periodId, pricing.TermCalendarId)).Rates;
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
    public async Task<HousingWorkspaceState> ApplyAssignmentsAsync(ApplyHousingDraftInput input)
    {
        if (!input.Confirmed) throw new UserFriendlyException("Review and confirm the Elements changes before applying them.");
        _writer.EnsureNonProductionWriteEnabled();
        var draft = await Find(input.PeriodId) ?? throw new UserFriendlyException("Refresh from Elements first.");
        CheckRevision(draft, input.Revision);
        var before = Read(draft.BaselineJson);
        var after = Read(draft.RoomsJson);
        Validate(before, after);

        // The vendor's assignment endpoint has no capacity or note fields. Pricing is posted
        // separately to the CampusFlow-owned billing batch, not to Elements' $0 room rent.
        if (before.Any(room => after.Single(x => x.Id == room.Id).Capacity != room.Capacity) ||
            after.SelectMany(x => x.Occupants).Any(x => !string.IsNullOrWhiteSpace(x.Notes)))
            throw new UserFriendlyException("This draft includes capacity or note changes that the Elements room-assignment API cannot apply. Save them as a CampusFlow draft, then resolve those changes before applying assignments and charges.");

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
        if (changedIds.Count == 0) return Map(draft);

        var pricing = await _elements.GetPricingAsync(input.PeriodId);
        var savedRates = await SavedRatesAsync(input.PeriodId, pricing.TermCalendarId);
        if (savedRates.Revision != input.RateRevision)
            throw new UserFriendlyException("Housing fee amounts changed since the preview. Review charges again before applying.");
        var configuredRates = savedRates.Rates;
        var documents = new[] { 137, 1471, 1765, pricing.SingleTransactionDocumentId };
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
        foreach (var id in roomChangedIds)
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

        var completed = 0;
        try
        {
            foreach (var id in roomChangedIds.Where(oldAssignments.ContainsKey))
            {
                var oldRoom = oldAssignments[id].room;
                await _writer.DeallocateAsync(id, input.PeriodId, oldRoom.Id);
                completed++;
                if (await _writer.GetAssignedRoomAsync(id, input.PeriodId) is not null)
                    throw new InvalidOperationException("Elements did not confirm removal of a room assignment.");
            }
            foreach (var id in roomChangedIds.Where(newAssignments.ContainsKey))
            {
                var newRoom = newAssignments[id].room;
                await _writer.AssignAsync(id, input.PeriodId, newRoom.Id);
                completed++;
                var confirmed = await _writer.GetAssignedRoomAsync(id, input.PeriodId);
                if (confirmed is null || !SameRoom(confirmed.Value, newRoom))
                    throw new InvalidOperationException("Elements did not confirm the new room assignment.");
            }
            foreach (var id in changedIds)
            {
                await _billingWriter.ReconcileAsync(id, pricing.TermCalendarId,
                    proposedCharges[id], documents);
                completed++;
            }
        }
        catch (Exception) when (completed > 0)
        {
            throw new UserFriendlyException("Elements may have applied some room or billing changes, but the operation did not finish. Review the live assignments and CampusFlow housing batch before retrying; do not blindly reapply this draft.");
        }

        draft.Refresh(JsonSerializer.Serialize(after), Clock.Now);
        await _drafts.UpdateAsync(draft, autoSave: true);
        return Map(draft);
    }
    public async Task<List<HousingStudentOption>> SearchStudentsAsync(string query, int periodId)
    {
        Tenant();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];
        return await _elements.SearchStudentsAsync(query.Trim(), periodId);
    }
    private static void CheckRevision(HousingAssignmentDraft draft, string? revision)
    {
        if (draft.ConcurrencyStamp != revision) throw new UserFriendlyException("Another user changed this draft. Reload before continuing.");
    }
    private static List<HousingWorkspaceChange> Validate(List<HousingWorkspaceRoom> before, List<HousingWorkspaceRoom> after)
    {
        try { return HousingWorkspacePlanner.Compare(before, after); }
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
    { Id = x.Id, PeriodId = x.PeriodId, Revision = x.ConcurrencyStamp, RefreshedAt = x.RefreshedAt, Rooms = Read(x.RoomsJson) };
}
