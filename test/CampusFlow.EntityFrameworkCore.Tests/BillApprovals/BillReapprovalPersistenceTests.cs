using System;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class BillReapprovalPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Repeated_staff_resets_allow_reapproval_and_preserve_accepted_history_and_artifacts()
    {
        var owner = Guid.NewGuid();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(owner);
        var approvals = GetRequiredService<IRepository<BillApproval, Guid>>();
        var profiles = GetRequiredService<IRepository<StudentProfile, Guid>>();
        var templates = GetRequiredService<IRepository<AgreementTemplate, Guid>>();
        var artifacts = GetRequiredService<IRepository<BillApprovalArtifact, Guid>>();
        var profileId = Guid.NewGuid(); var userId = Guid.NewGuid(); var templateId = Guid.NewGuid();
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid(); var thirdId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var acceptedAt = new DateTime(2026, 11, 11);
        BillApproval Approval(Guid id)
        {
            var approval = new BillApproval(id, owner, userId, profileId, "test", "test", "269", "B27C", "Spring 2027");
            approval.UpdateDraft(BillPaymentChoice.NoBalanceDue, 0, 0, 0, 0, 0, "[]", "{\"StudentName\":\"Test Student\"}");
            approval.Accept(templateId, 1, "Original agreement", acceptedAt, null, null);
            return approval;
        }
        await WithUnitOfWorkAsync(async () =>
        {
            await profiles.InsertAsync(new StudentProfile(profileId, owner, null,
                new(StudentInformationSystemProvider.ThesisElements, "test", "test", "student@example.test", "Test", null, "Student")), true);
            await templates.InsertAsync(new AgreementTemplate(templateId, owner, "Test", 1, acceptedAt, "Original agreement", "[]", true), true);
            await approvals.InsertAsync(Approval(firstId), true);
            await artifacts.InsertAsync(new BillApprovalArtifact(artifactId, owner, firstId), true);
        });

        var reset = GetRequiredService<IResetBillApprovalAppService>();
        await reset.ResetAsync(firstId);
        await WithUnitOfWorkAsync(async () => await approvals.InsertAsync(Approval(secondId), true));
        await reset.ResetAsync(secondId);
        await WithUnitOfWorkAsync(async () => await approvals.InsertAsync(Approval(thirdId), true));

        await WithUnitOfWorkAsync(async () =>
        {
            Assert.Equal(thirdId, Assert.Single(await approvals.GetListAsync()).Id);
            Assert.Equal(firstId, (await artifacts.GetAsync(artifactId)).BillApprovalId);
            using (GetRequiredService<IDataFilter>().Disable<Volo.Abp.ISoftDelete>())
            {
                Assert.Equal(3, (await approvals.GetListAsync()).Count);
                foreach (var id in new[] { firstId, secondId })
                {
                    var history = await approvals.GetAsync(id);
                    Assert.True(history.IsDeleted);
                    Assert.NotNull(history.DeletionTime);
                    Assert.Equal(acceptedAt, history.AcceptedAt);
                    Assert.Equal("Original agreement", history.RenderedAgreementSnapshot);
                    Assert.Contains("Test Student", history.ReviewSnapshotJson);
                }
            }
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => WithUnitOfWorkAsync(async () =>
            await approvals.InsertAsync(Approval(Guid.NewGuid()), true)));
    }
}
