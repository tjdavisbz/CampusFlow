using System.Linq;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.BillApprovals;

public class HousingContactTests
{
    [Fact]
    public void Missing_housing_tile_gives_the_requested_ResLife_contact()
    {
        var journey=new IndexModel.RegistrationJourneyViewModel {
            Term=new(StudentInformationSystemProvider.ThesisElements,"269","B27C","Spring 2027",new(2027,1,11),new(2027,4,30)),
            HousingRequired=true,HasHousingAssignment=false,BillApprovalEnabled=true};
        journey.BuildSteps();
        Assert.Equal("Contact reslife@nelson.edu for a housing assignment before approving your bill.",
            journey.Steps.Single(x=>x.Title=="Housing").Description);
        Assert.Null(journey.Steps.Single(x=>x.Title=="Bill approval").Url);
    }
}
