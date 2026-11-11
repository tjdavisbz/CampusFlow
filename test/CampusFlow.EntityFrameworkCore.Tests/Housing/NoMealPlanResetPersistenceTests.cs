using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class NoMealPlanResetPersistenceTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Clearing_None_preserves_its_history_and_allows_exactly_one_new_active_choice()
    {
        var tenantId = Guid.NewGuid();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(tenantId);
        var profiles = GetRequiredService<IRepository<StudentProfile, Guid>>();
        var selections = GetRequiredService<IRepository<StudentHousingSelection, Guid>>();
        var filter = GetRequiredService<IDataFilter>();
        var profileId = Guid.NewGuid(); var originalId = Guid.NewGuid(); var replacementId = Guid.NewGuid();
        StudentHousingSelection Selection(Guid id, int? planId, string name) => new(id, tenantId, profileId,
            "reset-test", "January 2027", new(2027, 1, 11), new(2027, 4, 30), HousingChoice.Commuter, planId, name);

        await WithUnitOfWorkAsync(async () =>
        {
            await profiles.InsertAsync(new(profileId, tenantId, null, new(StudentInformationSystemProvider.ThesisElements,
                "reset-test", "test", "student@example.test", "Test", null, "Student")), autoSave: true);
            var original = Selection(originalId, null, "None"); original.MarkSynced();
            await selections.InsertAsync(original, autoSave: true);
        });
        await WithUnitOfWorkAsync(async () => await selections.DeleteAsync(await selections.GetAsync(originalId), autoSave: true));
        await WithUnitOfWorkAsync(async () =>
        {
            Assert.Null(await selections.FindAsync(originalId));
            await selections.InsertAsync(Selection(replacementId, 20, "20 meals"), autoSave: true);
        });
        await WithUnitOfWorkAsync(async () =>
        {
            Assert.Equal(replacementId, Assert.Single(await selections.GetListAsync()).Id);
            using (filter.Disable<Volo.Abp.ISoftDelete>())
            {
                var original = await selections.GetAsync(originalId);
                Assert.True(original.IsDeleted);
                Assert.NotNull(original.DeletionTime);
                Assert.Null(original.ExternalMealPlanId);
                Assert.Equal("None", original.MealPlanName);
            }
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => WithUnitOfWorkAsync(async () =>
            await selections.InsertAsync(Selection(Guid.NewGuid(), 40, "40 meals"), autoSave: true)));
    }
}
