using System;
using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.Housing;

public sealed record HousingPartialRecoveryPlan(List<int> Changed, List<int> Removals, List<int> Assignments);

// A reviewed recovery may continue only along the original old -> unassigned -> new
// transition. Completed operations and unrelated occupants must not be replayed or changed.
public static class HousingPartialRecovery
{
    public static HousingPartialRecoveryPlan Review(List<HousingWorkspaceRoom> before,
        List<HousingWorkspaceRoom> desired, List<HousingWorkspaceRoom> current, IReadOnlyList<string> operations)
    {
        HousingWorkspacePlanner.Compare(before,desired);
        HousingWorkspacePlanner.Compare(before,current,requireCapacityCompliance:false);
        foreach(var room in before)
        {
            var target=desired.Single(x=>x.Id==room.Id);var live=current.Single(x=>x.Id==room.Id);
            if(target.Capacity!=room.Capacity||live.Capacity!=room.Capacity||target.Name!=room.Name||live.Name!=room.Name||
                target.Building!=room.Building||live.Building!=room.Building)
                throw new ArgumentException("Partial recovery requires unchanged room identities and capacities. The saved plan is retained.");
        }
        var old=Map(before);var next=Map(desired);var liveMap=Map(current);
        var changed=old.Keys.Union(next.Keys).Where(id=>!old.TryGetValue(id,out var a)||!next.TryGetValue(id,out var b)||
            a.Room!=b.Room||a.Person.Rate!=b.Person.Rate||a.Person.IntentionalSingle!=b.Person.IntentionalSingle).Order().ToList();
        if(changed.Count==0)throw new ArgumentException("There are no pending assignment or billing changes in this saved plan.");
        foreach(var id in old.Keys.Union(next.Keys).Union(liveMap.Keys))
        {
            var was=old.GetValueOrDefault(id).Room;var wanted=next.GetValueOrDefault(id).Room;var actual=liveMap.GetValueOrDefault(id).Room;
            if(!changed.Contains(id))
            {if(actual!=was)throw Conflict();continue;}
            if(actual!=0&&actual!=was&&actual!=wanted)throw Conflict();
            if(was==wanted&&actual!=was)throw Conflict();
            if(was!=wanted)
            {
                if(actual==was&&was!=0&&operations.Contains($"Room removal verified for {id}"))throw Conflict();
                if(actual==0&&was!=0&&!operations.Contains($"Starting room removal for {id}"))throw Conflict();
                if(actual==wanted&&wanted!=0&&!operations.Contains($"Starting room assignment for {id}"))throw Conflict();
                if(actual!=wanted&&operations.Contains($"Room assignment verified for {id}"))throw Conflict();
            }
        }
        var moving=changed.Where(id=>old.GetValueOrDefault(id).Room!=next.GetValueOrDefault(id).Room).ToList();
        return new(changed,moving.Where(id=>old.ContainsKey(id)&&liveMap.GetValueOrDefault(id).Room==old[id].Room).ToList(),
            moving.Where(id=>next.ContainsKey(id)&&liveMap.GetValueOrDefault(id).Room!=next[id].Room).ToList());
    }
    private static ArgumentException Conflict()=>new("A live assignment conflicts with the saved operation history. Recovery stopped without changing the plan or Elements.");
    private static Dictionary<int,(int Room,HousingWorkspaceOccupant Person)> Map(List<HousingWorkspaceRoom> rooms)=>
        rooms.SelectMany(r=>r.Occupants.Select(o=>(r.Id,Person:o))).ToDictionary(x=>x.Person.StudentUid,x=>x);
}
