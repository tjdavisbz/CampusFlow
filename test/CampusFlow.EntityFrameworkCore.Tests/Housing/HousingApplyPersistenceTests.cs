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
    [Fact]public async Task Recovery_retains_original_plan_operations_and_review_across_reload()
    {
        var tenantId=Guid.NewGuid();var id=Guid.NewGuid();var originalRun=Guid.NewGuid();var recoveryRun=Guid.NewGuid();
        using var tenant=GetRequiredService<ICurrentTenant>().Change(tenantId);
        var repository=GetRequiredService<IRepository<HousingAssignmentDraft,Guid>>();
        await WithUnitOfWorkAsync(async()=>
        {
            var draft=new HousingAssignmentDraft(id,tenantId,2,"original baseline",DateTime.UtcNow);draft.Save("all 113 desired assignments");
            var previous=new HousingApplyProgress{RunId=originalRun,Status="Needs review",BeforeJson=draft.BaselineJson,DesiredJson=draft.RoomsJson,FailureCode="conflict",Operations=["Room assignment verified for 1","Starting room assignment for 2"]};
            HousingApplyProgress.Archive(draft,previous);
            new HousingApplyProgress{RunId=recoveryRun,RecoveryOfRunId=originalRun,ReviewReason="Reviewed live rooms and batch",BeforeJson=draft.BaselineJson,DesiredJson=draft.RoomsJson}.Save(draft);
            await repository.InsertAsync(draft,autoSave:true);
        });
        await WithUnitOfWorkAsync(async()=>
        {
            var draft=await repository.GetAsync(id);Assert.Equal("all 113 desired assignments",draft.RoomsJson);Assert.Equal("original baseline",draft.BaselineJson);
            var current=HousingApplyProgress.Read(draft)!;Assert.Equal(originalRun,current.RecoveryOfRunId);Assert.Equal(recoveryRun,current.RunId);Assert.Equal("Reviewed live rooms and batch",current.ReviewReason);
            var history=System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HousingApplyProgress>>(draft.ExtraProperties["HousingApplyHistoryV1"]!.ToString()!)!;
            var original=Assert.Single(history);Assert.Equal(originalRun,original.RunId);Assert.Equal(draft.RoomsJson,original.DesiredJson);Assert.Equal("conflict",original.FailureCode);Assert.Equal(2,original.Operations.Count);
        });
    }
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
