using System;
using System.Collections.Generic;
using CampusFlow.Housing;
using Xunit;
namespace CampusFlow.Domain.Tests.Housing;
public class HousingAdministrationRulesTests
{
    private static HousingWorkspaceRoom Room(int id,int capacity,params int[] ids)=>new(){Id=id,Name=$"Room {id}",Building="Hall",Capacity=capacity,Occupants=new List<int>(ids).ConvertAll(x=>new HousingWorkspaceOccupant{StudentUid=x})};
    [Theory][InlineData("Commuter")][InlineData("Commuter Dorm")][InlineData(" commuter ")]
    public void Commuters_have_no_base_or_single_charge(string building)=>Assert.Empty(HousingChargeCalculator.Quote(269,building,"Standard",false,[],[],0));
    [Theory][InlineData("Standard",true)][InlineData("AIC Triple Room",false)][InlineData("Unreviewed",false)]
    public void A_commuter_cannot_receive_a_single_or_special_rate(string rate,bool single)=>Assert.Throws<ArgumentException>(()=>HousingChargeCalculator.Quote(269,"Commuter",rate,single,[],[],0));
    [Fact]public void An_unrelated_existing_over_capacity_room_does_not_block_an_assignment()
    {var changes=HousingWorkspacePlanner.Compare([Room(1,1),Room(2,1,1,2)],[Room(1,1,3),Room(2,1,1,2)]);Assert.Contains(changes,x=>x.Action=="Assign");}
    [Fact]public void New_occupants_in_an_existing_over_capacity_room_are_rejected()=>Assert.Throws<ArgumentException>(()=>HousingWorkspacePlanner.Compare([Room(1,1,1,2)],[Room(1,1,1,2,3)]));
    [Fact]public void Capacity_above_the_vendor_limit_is_rejected()=>Assert.Throws<ArgumentException>(()=>HousingWorkspacePlanner.Compare([Room(1,10)],[Room(1,150)]));
    [Theory][InlineData(-1)][InlineData(1001)]public void Invalid_capacity_cannot_even_be_saved(int capacity)=>Assert.Throws<ArgumentException>(()=>HousingWorkspacePlanner.Compare([Room(1,1)],[Room(1,capacity)],false));
    [Fact]public void Unknown_terms_remain_unapproved_for_commuter_billing()=>Assert.Throws<ArgumentException>(()=>HousingChargeCalculator.Quote(999,"Commuter","Standard",false,[],[],0));
    [Theory][InlineData("Running")][InlineData("Needs review")]
    public void Incomplete_apply_blocks_edit_and_reapply(string state)
    {
        var now=DateTime.UtcNow;var draft=new HousingAssignmentDraft(Guid.NewGuid(),Guid.NewGuid(),1,"[]",now);
        new HousingApplyProgress{RunId=Guid.NewGuid(),StartedAt=now,Status=state,Operations=["Starting assignment"]}.Save(draft);
        Assert.Throws<InvalidOperationException>(()=>HousingApplyProgress.EnsureEditable(draft,now));
        Assert.Equal("Starting assignment",Assert.Single(HousingApplyProgress.Read(draft)!.Operations));
    }
    [Fact]public void Active_apply_cannot_be_cleared_by_refresh_but_stale_apply_can_be_reviewed()
    {
        var now=DateTime.UtcNow;var draft=new HousingAssignmentDraft(Guid.NewGuid(),Guid.NewGuid(),1,"[]",now);
        new HousingApplyProgress{RunId=Guid.NewGuid(),StartedAt=now}.Save(draft);
        Assert.Throws<InvalidOperationException>(()=>HousingApplyProgress.EnsureEditable(draft,now,refreshing:true));
        Assert.True(HousingApplyProgress.Read(draft)!.CanRecover(now.AddMinutes(31)));
        Assert.Throws<InvalidOperationException>(()=>HousingApplyProgress.EnsureEditable(draft,now.AddMinutes(31),refreshing:true));
    }
    [Fact]public void Completed_apply_allows_new_draft_edits()
    {var now=DateTime.UtcNow;var draft=new HousingAssignmentDraft(Guid.NewGuid(),Guid.NewGuid(),1,"[]",now);new HousingApplyProgress{Status="Completed"}.Save(draft);HousingApplyProgress.EnsureEditable(draft,now);}
}
