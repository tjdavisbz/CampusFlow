using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.Housing;

public class MigratedResidentialHousingSourceTests
{
    [Theory] [InlineData(268)] [InlineData(270)] [InlineData(0)] [InlineData(-1)]
    public async Task Unreviewed_terms_do_not_read_or_import_legacy_assignments(int term)
    {
        var reader = new ElementsMigratedHousingReader(new ConfigurationBuilder().Build());
        Assert.Null(await reader.ReadForTermAsync(term));
    }

    [Fact] public async Task Missing_Elements_access_cannot_fall_back_to_a_CampusFlow_copy()
    {
        var reader = new ElementsMigratedHousingReader(new ConfigurationBuilder().Build());
        await Assert.ThrowsAnyAsync<System.Exception>(() => reader.ReadForTermAsync(269));
    }
}

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class MigratedResidentialHousingRegistrationTests : CampusFlowEntityFrameworkCoreTestBase
{
    [Fact] public void The_live_housing_reader_dependency_is_registered_with_the_Elements_provider()
    {
        Assert.IsType<ElementsMigratedHousingReader>(GetRequiredService<IElementsMigratedHousingReader>());
        Assert.IsType<ElementsHousingReader>(GetRequiredService<IElementsHousingReader>());
    }
}
