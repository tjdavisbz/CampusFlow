using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CampusFlow.Housing;
using Xunit;
namespace CampusFlow.Domain.Tests.Housing;
public class HousingPartialRecoveryTests
{
    private static List<HousingWorkspaceRoom> Rooms()=>[new(){Id=1,Building="Bridges",Name="101",Capacity=10,Occupants=Enumerable.Range(1,6).Select(id=>new HousingWorkspaceOccupant{StudentUid=id}).ToList()},new(){Id=2,Building="Bridges",Name="102",Capacity=10}];
    private static List<HousingWorkspaceRoom> Copy(List<HousingWorkspaceRoom> x)=>JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(x))!;
    [Fact]public void Six_removals_with_four_live_completions_only_resume_the_two_remaining_removals()
    {
        var before=Rooms();var desired=Copy(before);desired[0].Occupants.Clear();var current=Copy(before);current[0].Occupants.RemoveAll(x=>x.StudentUid<=4);
        var ops=Enumerable.Range(1,4).Select(x=>$"Starting room removal for {x}").ToList();ops.AddRange(Enumerable.Range(1,3).Select(x=>$"Room removal verified for {x}"));
        var review=HousingPartialRecovery.Review(before,desired,current,ops);Assert.Equal(new[]{5,6},review.Removals);Assert.Empty(review.Assignments);Assert.Equal(6,review.Changed.Count);
    }
    [Fact]public void A_move_between_its_removal_and_assignment_never_repeats_the_removal()
    {
        var before=Rooms();var desired=Copy(before);var person=desired[0].Occupants[0];desired[0].Occupants.RemoveAt(0);desired[1].Occupants.Add(person);var current=Copy(before);current[0].Occupants.RemoveAt(0);
        var review=HousingPartialRecovery.Review(before,desired,current,["Starting room removal for 1","Room removal verified for 1"]);Assert.Empty(review.Removals);Assert.Equal(new[]{1},review.Assignments);
    }
    [Theory][InlineData("unexpected-room")][InlineData("unrelated-student")][InlineData("unexplained-removal")][InlineData("reversed-completion")][InlineData("capacity")][InlineData("renamed")][InlineData("duplicate")]
    public void External_or_ambiguous_changes_stop_recovery(string problem)
    {
        var before=Rooms();var desired=Copy(before);desired[0].Occupants.RemoveAt(0);var current=Copy(before);var ops=new List<string>();
        switch(problem){case "unexpected-room":current[0].Occupants.RemoveAt(0);current[1].Occupants.Add(new(){StudentUid=1});break;case "unrelated-student":current[0].Occupants.RemoveAt(1);break;case "unexplained-removal":current[0].Occupants.RemoveAt(0);break;case "reversed-completion":ops.Add("Room removal verified for 1");break;case "capacity":current[0].Capacity=9;break;case "renamed":desired[0].Name="other";break;case "duplicate":current[1].Occupants.Add(new(){StudentUid=1});break;}
        Assert.Throws<ArgumentException>(()=>HousingPartialRecovery.Review(before,desired,current,ops));
    }
    [Fact]public void A_large_complete_plan_keeps_all_141_proposed_changes()
    {
        var before=Enumerable.Range(1,15).Select(n=>new HousingWorkspaceRoom{Id=n,Building="Bridges",Name=n.ToString(),Capacity=10}).ToList();
        for(var id=1;id<=141;id++)before[(id-1)/10].Occupants.Add(new(){StudentUid=id});
        var desired=Copy(before);foreach(var room in desired)room.Occupants.Clear();
        var review=HousingPartialRecovery.Review(before,desired,Copy(before),[]);Assert.Equal(141,review.Removals.Count);Assert.Equal(141,review.Changed.Count);
    }
}
