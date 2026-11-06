using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Housing;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class HousingApplyPersistenceTests:CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]public async Task Only_one_worker_can_persist_an_apply_claim_and_its_history_survives_reload()
    {
        var owner=Guid.NewGuid();var id=Guid.NewGuid();var run=Guid.NewGuid();using var tenant=GetRequiredService<ICurrentTenant>().Change(owner);
        var repository=GetRequiredService<IRepository<HousingAssignmentDraft,Guid>>();var units=GetRequiredService<IUnitOfWorkManager>();
        await WithUnitOfWorkAsync(async()=>await repository.InsertAsync(new(id,owner,1,"[]",DateTime.UtcNow),autoSave:true));
        using(var staleUnit=units.Begin(requiresNew:true,isTransactional:false))
        {
            var stale=await repository.GetAsync(id);
            using(var claimUnit=units.Begin(requiresNew:true,isTransactional:false))
            {
                var current=await repository.GetAsync(id);new HousingApplyProgress{RunId=run,StartedAt=DateTime.UtcNow,Operations=["Starting capacity increases"]}.Save(current);
                await repository.UpdateAsync(current,autoSave:true);await claimUnit.CompleteAsync();
            }
            new HousingApplyProgress{RunId=Guid.NewGuid(),StartedAt=DateTime.UtcNow}.Save(stale);
            await Assert.ThrowsAsync<AbpDbConcurrencyException>(()=>repository.UpdateAsync(stale,autoSave:true));
        }
        await WithUnitOfWorkAsync(async()=>
        {
            var progress=HousingApplyProgress.Read(await repository.GetAsync(id))!;Assert.Equal(run,progress.RunId);Assert.Equal("Running",progress.Status);Assert.Equal("Starting capacity increases",Assert.Single(progress.Operations));
        });
    }
}
