using System;
using System.Threading.Tasks;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class PaymentTermPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Posting_term_survives_a_database_round_trip_without_a_new_column()
    {
        var tenant=GetRequiredService<ICurrentTenant>();var owner=Guid.NewGuid();var id=Guid.NewGuid();
        var profiles=GetRequiredService<IRepository<StudentProfile,Guid>>();var payments=GetRequiredService<IRepository<PayflowPayment,Guid>>();
        using(tenant.Change(owner))
        {
            await WithUnitOfWorkAsync(async()=>{
                var profile=new StudentProfile(Guid.NewGuid(),owner,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@example.test","Test",null,"Student"));
                await profiles.InsertAsync(profile,autoSave:true);
                var payment=new PayflowPayment(id,owner,Guid.NewGuid(),profile.Id,"10",50,0,"USD","request",true);
                payment.SetElementsTerm(268);await payments.InsertAsync(payment,autoSave:true);
            });
            await WithUnitOfWorkAsync(async()=>Assert.Equal(268,(await payments.GetAsync(id)).ElementsTermId));
        }
    }
}
