using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CampusFlow.CostTypes;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.CostTypes;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class CostTypeSavePersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact] public async Task First_save_and_subsequent_edit_survive_separate_transactions()
    {
        using var tenant=GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid());
        var configs=GetRequiredService<IRepository<CostTypeConfiguration,Guid>>();
        var elements=Substitute.For<ICostTypeElements>();
        elements.GetChoicesAsync().Returns(new List<CostTypeChoice>{new(4,"DE","Distance Education")});
        var service=new CostTypeAppService(configs,GetRequiredService<IRepository<CostTypeRun,Guid>>(),elements,GetRequiredService<IUnitOfWorkManager>())
        {LazyServiceProvider=GetRequiredService<IAbpLazyServiceProvider>()};
        await WithUnitOfWorkAsync(async()=>
        {
            await service.SaveAsync(new(){ChangeReason="Initial review",Rules=[new(){Priority=1,Attendance="Distance Education",CostType="DE",CostTypeId=4},new(){Priority=2,Attendance="School of Ministry",CostType="LAWTON"}]});
        });
        await WithUnitOfWorkAsync(async()=>
        {
            var loaded=await service.GetAsync();Assert.NotEmpty(loaded.Revision);Assert.Equal(2,loaded.Rules.Count);Assert.Null(loaded.Rules[1].CostTypeId);
            loaded.ChangeReason="Retain unresolved mapping for review";loaded.Rules[1].Enabled=false;await service.SaveAsync(loaded);
        });
        await WithUnitOfWorkAsync(async()=>
        {
            var loaded=await service.GetAsync();Assert.False(loaded.Rules[1].Enabled);Assert.Equal(4,loaded.Rules[0].CostTypeId);
            Assert.Single(await configs.GetListAsync());
        });
    }
}
