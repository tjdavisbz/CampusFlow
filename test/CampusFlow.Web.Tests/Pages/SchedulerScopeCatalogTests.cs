using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.CourseSelections;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.Pages;
public class SchedulerScopeCatalogTests
{
    [Fact]
    public async Task A_retired_catalog_entry_remains_selected_in_saved_access_restrictions()
    {
        var assignment = new AdvisorAssignment(Guid.NewGuid(), Guid.NewGuid(), "Residential", "1", "scheduler@example.test", "Scheduler", DateTime.UtcNow.AddDays(-1));
        assignment.ConfigurePrimaryProgramScope(new([999], [998], [997]));
        var assignments = Substitute.For<IRepository<AdvisorAssignment,Guid>>();
        assignments.GetListAsync(Arg.Any<Expression<Func<AdvisorAssignment,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<AdvisorAssignment>{assignment});
        var terms = Substitute.For<IRepository<RegistrationTermConfiguration,Guid>>();
        terms.GetListAsync(Arg.Any<bool>(),Arg.Any<CancellationToken>()).Returns(new List<RegistrationTermConfiguration>());
        var lookup = Substitute.For<IStudentPrimaryProgramLookup>();lookup.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        lookup.GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(new StudentProgramChoice[]{new(10,"Current major",20,"BA",30,"Current program")});
        var tenant = Substitute.For<ICurrentTenant>();tenant.Name.Returns("nelson");
        var lazy = Substitute.For<IAbpLazyServiceProvider>();lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var page = new AdvisorVisibilityModel(assignments,terms,Substitute.For<IStudentInformationSystemAdvisorLookup>(),Substitute.For<ITenantThemeProvider>(),Substitute.For<IGuidGenerator>(),[lookup])
            {PageContext=new PageContext{HttpContext=new DefaultHttpContext()},LazyServiceProvider=lazy};
        await page.OnGetAsync();
        Assert.Contains(page.ProgramChoices,x=>x.Id==999);
        Assert.Contains(page.ProgramChoices,x=>x.DegreeId==998);
        Assert.Contains(page.ProgramChoices,x=>x.BlockProgramId==997);
        var row = page.Rows.Single(x=>x.AttendanceType=="Residential");
        var saved = JsonSerializer.Deserialize<Dictionary<string,PrimaryProgramScope>>(row.ProgramScopesJson)!;
        Assert.Equal(999,Assert.Single(saved["scheduler@example.test"].MajorIds));
        Assert.Equal(998,Assert.Single(saved["scheduler@example.test"].DegreeIds));
        Assert.Equal(997,Assert.Single(saved["scheduler@example.test"].ProgramIds));
    }
}
