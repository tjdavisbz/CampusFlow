using CampusFlow.Samples;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Applications;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<CampusFlowEntityFrameworkCoreTestModule>
{

}
