using System;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.BillApprovals;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class LegacyBillApprovalPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Historical_rows_need_no_portal_account_and_retain_timestamps_and_tenant_isolation()
    {
        var tenant=GetRequiredService<ICurrentTenant>();
        var repo=GetRequiredService<IRepository<LegacyBillApproval,Guid>>();
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var time=new DateTime(2006,5,12,20,15,33,123,DateTimeKind.Unspecified);
        LegacyBillApproval Row(Guid owner) => new(Guid.NewGuid(),owner,"10","student","7","B06Q","Fall 2006",true,null,time,new string('a',64),2);
        using(tenant.Change(a))await WithUnitOfWorkAsync(async()=>{await repo.InsertAsync(Row(a),autoSave:true);});
        using(tenant.Change(b))await WithUnitOfWorkAsync(async()=>{await repo.InsertAsync(Row(b),autoSave:true);});
        using(tenant.Change(a))await WithUnitOfWorkAsync(async()=>{
            var rows=await repo.GetListAsync(x=>x.ExternalStudentId=="10");
            rows.Count.ShouldBe(1); rows[0].TenantId.ShouldBe(a);
            rows[0].AcceptTime.ShouldBe(time); rows[0].LoginTime.ShouldBeNull();
            rows[0].SourceApproved.ShouldBeTrue();rows[0].SourceRowNumber.ShouldBe(2);
        });
    }
    [Fact]
    public async Task Database_rejects_a_second_row_for_the_same_tenant_student_and_term()
    {
        var tenant=GetRequiredService<ICurrentTenant>();var owner=Guid.NewGuid();
        var repo=GetRequiredService<IRepository<LegacyBillApproval,Guid>>();
        LegacyBillApproval Row() => new(Guid.NewGuid(),owner,"10","student","7","B06Q","Fall 2006",true,null,DateTime.Now,new string('a',64),2);
        using(tenant.Change(owner))
        {
            await WithUnitOfWorkAsync(async()=>{await repo.InsertAsync(Row(),autoSave:true);});
            await Should.ThrowAsync<DbUpdateException>(()=>WithUnitOfWorkAsync(async()=>{await repo.InsertAsync(Row(),autoSave:true);}));
        }
    }
}
