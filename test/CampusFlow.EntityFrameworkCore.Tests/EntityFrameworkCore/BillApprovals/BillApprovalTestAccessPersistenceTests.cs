using System;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.BillApprovals;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class BillApprovalTestAccessPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Test_access_round_trips_in_existing_extra_properties_and_remains_tenant_scoped()
    {
        var tenant=GetRequiredService<ICurrentTenant>();var owner=Guid.NewGuid();var id=Guid.NewGuid();
        var configurations=GetRequiredService<IRepository<BillApprovalTermConfiguration,Guid>>();
        var agreements=GetRequiredService<IRepository<AgreementTemplate,Guid>>();
        var policies=GetRequiredService<IRepository<PaymentPlanPolicy,Guid>>();
        using(tenant.Change(owner))
        {
            await WithUnitOfWorkAsync(async()=>
            {
                var agreement=new AgreementTemplate(Guid.NewGuid(),owner,"Test",1,DateTime.Today,"Agreement","[]",true);
                var policy=new PaymentPlanPolicy(Guid.NewGuid(),owner,"Test",1,DateTime.Today,100,3,3500,1500,"[]","[]","[]","[]",true);
                await agreements.InsertAsync(agreement,autoSave:true);await policies.InsertAsync(policy,autoSave:true);
                var term=new BillApprovalTermConfiguration(id,owner,"269","B27S","Spring 2027",DateTime.Today.AddDays(1),DateTime.Today.AddDays(30),false,agreement.Id,policy.Id);
                term.ConfigureTestAccess(true,new[]{"100","200"});await configurations.InsertAsync(term,autoSave:true);
            });
            await WithUnitOfWorkAsync(async()=>
            {
                var loaded=await configurations.GetAsync(id);
                Assert.True(loaded.TestAccessEnabled);Assert.True(loaded.IsOpenForStudent(DateTime.Today,"100"));
                Assert.False(loaded.IsOpenForStudent(DateTime.Today,"999"));Assert.False(loaded.IsOpen(DateTime.Today));
            });
        }
        using(tenant.Change(Guid.NewGuid()))await WithUnitOfWorkAsync(async()=>Assert.Null(await configurations.FindAsync(id)));
    }
}
