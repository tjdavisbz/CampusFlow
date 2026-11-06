using System;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;
[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class CompletionClaimPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact] public async Task A_second_stale_worker_cannot_overwrite_a_committed_delivery_claim()
    {
        var tenant=GetRequiredService<ICurrentTenant>();var owner=Guid.NewGuid();var id=Guid.NewGuid();
        var approvals=GetRequiredService<IRepository<BillApproval,Guid>>();var profiles=GetRequiredService<IRepository<StudentProfile,Guid>>();
        var templates=GetRequiredService<IRepository<AgreementTemplate,Guid>>();var units=GetRequiredService<IUnitOfWorkManager>();
        using var scope=tenant.Change(owner);
        await WithUnitOfWorkAsync(async()=>
        {
            var profile=new StudentProfile(Guid.NewGuid(),owner,null,new(StudentInformationSystemProvider.ThesisElements,"10","100","student@nelson.edu","Test",null,"Student"));
            var template=new AgreementTemplate(Guid.NewGuid(),owner,"Test",1,DateTime.Today,"Agreement","[]",true);
            await profiles.InsertAsync(profile,autoSave:true);await templates.InsertAsync(template,autoSave:true);
            var approval=new BillApproval(id,owner,Guid.NewGuid(),profile.Id,"10","100","269","B27S","Spring 2027");
            approval.Accept(template.Id,1,"Agreement",DateTime.UtcNow,null,null);await approvals.InsertAsync(approval,autoSave:true);
        });
        using(var staleUnit=units.Begin(requiresNew:true,isTransactional:false))
        {
            var stale=await approvals.GetAsync(id);
            using(var claimUnit=units.Begin(requiresNew:true,isTransactional:false))
            {
                var current=await approvals.GetAsync(id);var progress=BillApprovalCompletionProgress.Read(current);
                progress.Begin(Guid.NewGuid(),DateTime.UtcNow);progress.Finish(BillCompletionStep.StudentEmail,BillCompletionStatus.Running);progress.Save(current);
                await approvals.UpdateAsync(current,autoSave:true);await claimUnit.CompleteAsync();
            }
            var old=BillApprovalCompletionProgress.Read(stale);old.Begin(Guid.NewGuid(),DateTime.UtcNow);old.Save(stale);
            await Assert.ThrowsAsync<AbpDbConcurrencyException>(()=>approvals.UpdateAsync(stale,autoSave:true));
        }
        await WithUnitOfWorkAsync(async()=>
        {
            var saved=await approvals.GetAsync(id);Assert.Equal(BillApprovalStatus.Approved,saved.Status);
            Assert.Equal(BillCompletionStatus.Running,BillApprovalCompletionProgress.Read(saved).Step(BillCompletionStep.StudentEmail).Status);
        });
    }
}
