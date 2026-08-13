using CampusFlow.Samples;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Domains;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<CampusFlowEntityFrameworkCoreTestModule>
{

}
