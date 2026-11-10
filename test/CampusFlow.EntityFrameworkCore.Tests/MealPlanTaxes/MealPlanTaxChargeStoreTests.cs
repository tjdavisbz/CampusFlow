using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.MealPlanTaxes;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class MealPlanTaxChargeStoreTests:CampusFlow.EntityFrameworkCore.CampusFlowEntityFrameworkCoreTestBase
{
    private static readonly MealPlanTaxTarget Target=new(10,269,42,8,2475m);
    [Fact]public async Task Claim_and_uncertainty_survive_new_store_instances_and_never_grant_a_second_send()
    {
        using var tenant=GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(),"nelson");
        var first=await GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target);Assert.True(first.MaySend);
        var second=await GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target);Assert.Equal(first.Id,second.Id);Assert.False(second.MaySend);
        await GetRequiredService<IMealPlanTaxChargeStore>().MarkUncertainAsync(first.Id);
        Assert.False((await GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target)).MaySend);
        await GetRequiredService<IMealPlanTaxChargeStore>().VerifyAsync(first.Id,"Batch:123");
        await WithUnitOfWorkAsync(async()=>{var row=await GetRequiredService<IRepository<MealPlanTaxCharge,Guid>>().GetAsync(first.Id);Assert.Equal("Verified",row.State);Assert.Equal("Batch:123",row.ExternalEntryId);});
    }
    [Fact]public async Task Database_rejects_duplicate_assignment_attempts()
    {
        var owner=Guid.NewGuid();using var tenant=GetRequiredService<ICurrentTenant>().Change(owner,"nelson");
        await GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target);
        await Assert.ThrowsAsync<DbUpdateException>(()=>WithUnitOfWorkAsync(async()=>await GetRequiredService<IRepository<MealPlanTaxCharge,Guid>>().InsertAsync(new MealPlanTaxCharge(Guid.NewGuid(),owner,Target),true)));
    }
    [Fact]public async Task A_changed_price_cannot_reuse_an_old_attempt()
    {
        using var tenant=GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(),"nelson");await GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target);
        await Assert.ThrowsAsync<Volo.Abp.UserFriendlyException>(()=>GetRequiredService<IMealPlanTaxChargeStore>().ClaimAsync(Target with{BaseAmount=2575m}));
    }
}
