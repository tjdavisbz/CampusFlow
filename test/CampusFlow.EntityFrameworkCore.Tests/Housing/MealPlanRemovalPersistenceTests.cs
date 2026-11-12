using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.ClearanceResets;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class MealPlanRemovalPersistenceTests:CampusFlowEntityFrameworkCoreTestBase
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Removal_persists_review_before_writes_and_recovers_a_lost_success_without_duplicate_deletions(bool lostReply)
    {
        var owner=Guid.NewGuid();using var tenant=GetRequiredService<ICurrentTenant>().Change(owner,"nelson");
        var repository=GetRequiredService<IRepository<MealPlanRemovalOperation,Guid>>();var units=GetRequiredService<IUnitOfWorkManager>();
        var target=new MealPlanRemovalTarget(10,269,42,14,"Plan",715m);
        var remaining=new List<MealPlanRemovalEntry>{new(1,100,165,0,715,"","Meal Plan","meal plan"),new(2,200,261,8047,58.99m,"CFMT-269-10-42","Meal plan sales tax (8.25%)","sales tax")};
        var original=JsonSerializer.Serialize(remaining);bool assigned=true,lost=false;var deleted=new List<int>();
        var assignments=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
        assignments.GetAssignmentsAsync("10",Arg.Any<CancellationToken>()).Returns(_=>Task.FromResult<IReadOnlyList<StudentMealPlanAssignment>>(assigned?[new(42,14,269,"Plan",715)]:[]));
        assignments.RemoveAsync("10",269,42,14,Arg.Any<CancellationToken>()).Returns(async _=>{
            using var read=units.Begin(requiresNew:true,isTransactional:false);
            var operation=Assert.Single(await repository.GetListAsync());Assert.Equal(original,operation.EntriesJson);Assert.Equal("Pending",operation.State);assigned=false;
        });
        var gateway=Substitute.For<IMealPlanRemovalGateway>();gateway.ReviewAsync(target).Returns(_=>remaining.ToList());
        gateway.DeleteAsync(target,Arg.Any<MealPlanRemovalEntry>()).Returns(call=>{
            var entry=call.Arg<MealPlanRemovalEntry>();Assert.Contains(entry,remaining);remaining.Remove(entry);deleted.Add(entry.Id);
            if(lostReply&&!lost){lost=true;throw new InvalidOperationException("Lost reply after successful delete");}return Task.CompletedTask;
        });
        var protection=Substitute.For<IClearanceResetProtection>();protection.AcquireAsync(10,269,Arg.Any<CancellationToken>()).Returns(_=>Task.FromResult<IAsyncDisposable>(new Lease()));
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());
        var coordinator=new MealPlanRemovalCoordinator(repository,gateway,assignments,protection,units,GetRequiredService<ICurrentTenant>(),user,GetRequiredService<IGuidGenerator>());
        var preview=await coordinator.ReviewAsync(target);
        if(lostReply)
        {
            await Assert.ThrowsAsync<UserFriendlyException>(()=>coordinator.RemoveAsync(target,preview.Revision,"Student requested removal"));
            Assert.Single(await coordinator.PendingAsync(10,269));Assert.False(assigned);Assert.Single(remaining);
            preview=await coordinator.ReviewAsync(target);
        }
        await coordinator.RemoveAsync(target,preview.Revision,"Reviewed cleanup after student request");
        Assert.Empty(remaining);Assert.Empty(await coordinator.PendingAsync(10,269));Assert.Equal(new[]{1,2},deleted);
        await assignments.Received(1).RemoveAsync("10",269,42,14,Arg.Any<CancellationToken>());
        await WithUnitOfWorkAsync(async()=>{var row=Assert.Single(await repository.GetListAsync());Assert.Equal("Completed",row.State);Assert.Equal(original,row.EntriesJson);Assert.Equal(JsonSerializer.Serialize(target),row.TargetJson);});
    }
    [Fact]
    public async Task A_changed_charge_invalidates_the_preview_before_any_claim_or_external_removal()
    {
        using var scope=GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(),"nelson");
        var repository=GetRequiredService<IRepository<MealPlanRemovalOperation,Guid>>();var units=GetRequiredService<IUnitOfWorkManager>();
        var target=new MealPlanRemovalTarget(10,269,42,14,"Plan",715m);
        var rows=new List<MealPlanRemovalEntry>{new(1,100,165,0,715,"","Meal Plan","meal plan")};
        var assignments=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
        assignments.GetAssignmentsAsync("10",Arg.Any<CancellationToken>()).Returns(new StudentMealPlanAssignment[]{new(42,14,269,"Plan",715)});
        var gateway=Substitute.For<IMealPlanRemovalGateway>();gateway.ReviewAsync(target).Returns(_=>rows.ToList());
        var protection=Substitute.For<IClearanceResetProtection>();protection.AcquireAsync(10,269,Arg.Any<CancellationToken>()).Returns(Task.FromResult<IAsyncDisposable>(new Lease()));
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());
        var coordinator=new MealPlanRemovalCoordinator(repository,gateway,assignments,protection,units,GetRequiredService<ICurrentTenant>(),user,GetRequiredService<IGuidGenerator>());
        var preview=await coordinator.ReviewAsync(target);rows[0]=rows[0] with{Amount=700};
        await Assert.ThrowsAsync<UserFriendlyException>(()=>coordinator.RemoveAsync(target,preview.Revision,"Reviewed prior charge preview"));
        await assignments.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);
        await gateway.DidNotReceiveWithAnyArgs().DeleteAsync(default!,default!);
        await WithUnitOfWorkAsync(async()=>Assert.Empty(await repository.GetListAsync()));
    }
    private sealed class Lease:IAsyncDisposable{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
}
