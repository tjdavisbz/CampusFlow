using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;
namespace CampusFlow.Application.Tests.Housing;
public class AdminMealPlanRemovalTests
{
    private readonly IStudentInformationSystemMealPlanRemovalService _removal=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();
    private readonly IStudentInformationSystemMealPlanService _plans=Substitute.For<IStudentInformationSystemMealPlanService>();
    private readonly AdminMealPlanAppService _service;
    public AdminMealPlanRemovalTests()
    {
        var students=Substitute.For<IStudentInformationSystemStudentLookup>();students.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        students.FindByExternalStudentIdAsync("123",Arg.Any<CancellationToken>()).Returns(new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements,"123","test","student@nelson.edu","Test",null,"Student"));
        _plans.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _plans.GetContextAsync("123",Arg.Any<int?>(),Arg.Any<CancellationToken>()).Returns(new StudentMealPlanContext("Commuter",[],[new(269,"Spring 2027",new(2027,1,1),new(2027,5,1))],269,[],null));
        _removal.IsEnabled.Returns(true);_removal.GetAssignmentsAsync("123",Arg.Any<CancellationToken>()).Returns(new StudentMealPlanAssignment[]{new(10,20,269,"Spring plan",100),new(11,21,268,"Fall plan",100)});
        var lazy=Substitute.For<IAbpLazyServiceProvider>();var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        _service=new([students],[_plans],NullLogger<AdminMealPlanAppService>.Instance,_removal){LazyServiceProvider=lazy};
    }
    private static RemoveStudentMealPlanInput Input()=>new(){ExternalStudentId="123",TermCalendarId=269,AssignmentId=10,MealPlanId=20,Confirmed=true};
    [Fact]public async Task Removal_never_changes_housing_status_or_assigns_a_new_plan()
    {await _service.RemoveAsync(Input());await _removal.Received(1).RemoveAsync("123",269,10,20,Arg.Any<CancellationToken>());await _plans.DidNotReceiveWithAnyArgs().UpdateHousingStatusAsync(default!,default,default);await _plans.DidNotReceiveWithAnyArgs().AssignAsync(default!,default);}
    [Fact]public async Task Missing_confirmation_is_rejected_before_removal()
    {var input=Input();input.Confirmed=false;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(input));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public async Task Another_terms_assignment_cannot_be_removed()
    {var input=Input();input.AssignmentId=11;input.MealPlanId=21;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(input));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public async Task Disabled_feature_is_rejected_before_removal()
    {_removal.IsEnabled.Returns(false);await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(Input()));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public void Removal_requires_its_own_permission()
    {Assert.Contains(typeof(AdminMealPlanAppService).GetMethod(nameof(AdminMealPlanAppService.RemoveAsync))!.GetCustomAttributes<AuthorizeAttribute>(),x=>x.Policy==CampusFlowPermissions.Admin.RemoveStudentMealPlan);}
}
