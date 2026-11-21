using System;
using System.Collections.Generic;
using System.Text.Json;
using CampusFlow.Housing;
using Xunit;
namespace CampusFlow.Domain.Tests.Housing;
public class HousingBillingRecoveryTests
{
    private static List<HousingWorkspaceRoom> Rooms()=>[new(){Id=1,Name="101",Building="Bridges Hall",Capacity=2,Occupants=[new(){StudentUid=10,Rate="Standard"},new(){StudentUid=11,Rate="Standard"}]},new(){Id=2,Name="102",Building="Bridges Hall",Capacity=2}];
    private static List<HousingWorkspaceRoom> Copy(List<HousingWorkspaceRoom> x)=>JsonSerializer.Deserialize<List<HousingWorkspaceRoom>>(JsonSerializer.Serialize(x))!;
    private static List<string> Proof()=>["Room removal verified for 10","Room removal verified for 11","Room assignment verified for 10","Starting charge reconciliation for 10"];
    [Fact] public void Completed_move_and_removal_can_finish_billing_without_room_changes()
    {var before=Rooms();var desired=Copy(before);var moving=desired[0].Occupants[0];desired[0].Occupants.Clear();desired[1].Occupants.Add(moving);Assert.Equal(new[]{10,11},HousingBillingRecovery.Review(before,desired,Copy(desired),Proof()));}
    [Theory][InlineData("missing-removal-proof")][InlineData("missing-assignment-proof")][InlineData("missing-billing-phase")][InlineData("room-conflict")][InlineData("extra-student")][InlineData("capacity")][InlineData("renamed-room")]
    public void Incomplete_or_changed_room_state_never_allows_billing_only(string problem)
    {
        var before=Rooms();var desired=Copy(before);var moving=desired[0].Occupants[0];desired[0].Occupants.Clear();desired[1].Occupants.Add(moving);var current=Copy(desired);var ops=Proof();
        switch(problem){case "missing-removal-proof":ops.RemoveAt(0);break;case "missing-assignment-proof":ops.RemoveAt(2);break;case "missing-billing-phase":ops.RemoveAt(3);break;case "room-conflict":current[1].Occupants.Clear();break;case "extra-student":current[0].Occupants.Add(new(){StudentUid=99,Rate="Standard"});break;case "capacity":desired[0].Capacity=current[0].Capacity=3;break;case "renamed-room":current[0].Name="Other";break;}
        Assert.Throws<ArgumentException>(()=>HousingBillingRecovery.Review(before,desired,current,ops));
    }
}
