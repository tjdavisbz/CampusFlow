using Xunit;

namespace CampusFlow.EntityFrameworkCore;

[CollectionDefinition(CampusFlowTestConsts.CollectionDefinitionName)]
public class CampusFlowEntityFrameworkCoreCollection : ICollectionFixture<CampusFlowEntityFrameworkCoreFixture>
{

}
