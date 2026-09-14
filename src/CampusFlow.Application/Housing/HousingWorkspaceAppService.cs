using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace CampusFlow.Housing;

[Authorize(CampusFlowPermissions.Admin.HousingAssignments)]
public class HousingWorkspaceAppService : CampusFlowAppService, IHousingWorkspaceAppService
{
    private readonly IRepository<HousingAssignmentDraft, Guid> _drafts;
    private readonly IElementsHousingReader _elements;
    private readonly IStudentInformationSystemStudentLookup _students;
    public HousingWorkspaceAppService(IRepository<HousingAssignmentDraft, Guid> drafts, IElementsHousingReader elements,
        IEnumerable<IStudentInformationSystemStudentLookup> students)
    {
        _drafts = drafts; _elements = elements;
        _students = students.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
    }
    private Guid Tenant() => CurrentTenant.Id ?? throw new UserFriendlyException("Select a university before managing housing.");
    private async Task<HousingAssignmentDraft?> Find(int periodId)
    {
        var tenant = Tenant();
        return await _drafts.FindAsync(x => x.TenantId == tenant && x.PeriodId == periodId);
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
        return Validate(Read(draft.BaselineJson), Read(draft.RoomsJson));
    }
    public async Task<List<AdminMealPlanStudentDto>> SearchStudentsAsync(string query)
    {
        Tenant();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];
        return (await _students.SearchAsync(query.Trim())).Select(x => new AdminMealPlanStudentDto
        { ExternalStudentId = x.ExternalStudentId, StudentId = x.StudentId, Name = $"{x.FirstName} {x.LastName}" }).ToList();
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
    private static HousingWorkspaceState Map(HousingAssignmentDraft x) => new()
    { Id = x.Id, PeriodId = x.PeriodId, Revision = x.ConcurrencyStamp, RefreshedAt = x.RefreshedAt, Rooms = Read(x.RoomsJson) };
}
