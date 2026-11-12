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
    private readonly IMealPlanRemovalCoordinator _coordinator=MealPlanRemovalTestDouble.Create();
    private readonly Volo.Abp.Domain.Repositories.IRepository<StudentHousingSelection,Guid> _locals=Substitute.For<Volo.Abp.Domain.Repositories.IRepository<StudentHousingSelection,Guid>>();
    private readonly List<StudentHousingSelection> _choices=[];
    public AdminMealPlanRemovalTests()
    {
        var students=Substitute.For<IStudentInformationSystemStudentLookup>();students.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        students.FindByExternalStudentIdAsync("123",Arg.Any<CancellationToken>()).Returns(new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements,"123","test","student@nelson.edu","Test",null,"Student"));
        _plans.Provider.Returns(StudentInformationSystemProvider.ThesisElements);
        _plans.GetContextAsync("123",Arg.Any<int?>(),Arg.Any<CancellationToken>()).Returns(new StudentMealPlanContext("Commuter",[],[new(269,"Spring 2027",new(2027,1,1),new(2027,5,1))],269,[],null));
        _removal.IsEnabled.Returns(true);_removal.GetAssignmentsAsync("123",Arg.Any<CancellationToken>()).Returns(new StudentMealPlanAssignment[]{new(10,20,269,"Spring plan",100),new(11,21,268,"Fall plan",100)});
        var lazy=Substitute.For<IAbpLazyServiceProvider>();var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        _locals.GetListAsync(Arg.Any<System.Linq.Expressions.Expression<Func<StudentHousingSelection,bool>>>(),Arg.Any<bool>(),Arg.Any<CancellationToken>())
            .Returns(call => _choices.Where(call.Arg<System.Linq.Expressions.Expression<Func<StudentHousingSelection,bool>>>().Compile()).ToList());
        _service=new([students],[_plans],NullLogger<AdminMealPlanAppService>.Instance,_removal,_locals,_coordinator){LazyServiceProvider=lazy};
    }
    private static RemoveStudentMealPlanInput Input()=>new(){ExternalStudentId="123",TermCalendarId=269,AssignmentId=10,MealPlanId=20,Confirmed=true,ReviewRevision="review",Reason="Reviewed assignment and charges"};
    private StudentHousingSelection Choice(string student="123",int? plan=20,int year=2027)
    {
        var choice=new StudentHousingSelection(Guid.NewGuid(),null,Guid.NewGuid(),student,$"January {year}",
            new(year,1,1),new(year,5,1),HousingChoice.Commuter,plan,"Plan");
        choice.MarkSynced();_choices.Add(choice);return choice;
    }
    [Fact]public async Task Confirmed_removal_soft_deletes_only_matching_student_term_and_paid_plan_choices()
    {
        var matching=Choice();Choice("456");Choice(plan:null);Choice(plan:21);Choice(year:2026);
        await _service.RemoveAsync(Input());
        await _locals.Received(1).DeleteAsync(matching,true,Arg.Any<CancellationToken>());
        Assert.Single(_locals.ReceivedCalls(),x=>x.GetMethodInfo().Name=="DeleteAsync");
    }
    [Fact]public async Task Failed_or_unconfirmed_external_removal_keeps_the_local_choice()
    {
        Choice();_coordinator.RemoveAsync(Arg.Any<MealPlanRemovalTarget>(),"review",Arg.Any<string>()).Returns(Task.FromException(new InvalidOperationException("Not confirmed")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>_service.RemoveAsync(Input()));
        await _locals.DidNotReceiveWithAnyArgs().DeleteAsync(default(StudentHousingSelection)!,default,default);
    }
    [Fact]public async Task Explicit_term_overrides_the_administrative_default()
    {
        await _service.GetContextAsync("123",268);
        await _plans.Received(1).GetContextAsync("123",268,Arg.Any<CancellationToken>());
        await _plans.DidNotReceive().GetContextAsync("123",269,Arg.Any<CancellationToken>());
    }
    [Fact]public async Task Missing_term_defaults_administration_to_Spring_2027_without_changing_the_service_default()
    {
        var terms=new StudentMealPlanTerm[]{new(268,"Fall 2026",new(2026,8,1),new(2026,12,1)),
            new(269,"Spring 2027",new(2027,1,1),DateTime.Today.AddYears(1))};
        _plans.GetContextAsync("123",Arg.Any<int?>(),Arg.Any<CancellationToken>()).Returns(call=>
            new StudentMealPlanContext("Commuter",[],terms,call.Arg<int?>()??268,[],null));
        Assert.Equal(269,(await _service.GetContextAsync("123")).SelectedTermCalendarId);
        await _plans.Received(1).GetContextAsync("123",null,Arg.Any<CancellationToken>());
        await _plans.Received(1).GetContextAsync("123",269,Arg.Any<CancellationToken>());
        await _plans.DidNotReceiveWithAnyArgs().AssignAsync(default!,default,default);
    }
    [Fact]public async Task Removal_never_changes_housing_status_or_assigns_a_new_plan()
    {await _service.RemoveAsync(Input());await _coordinator.Received(1).RemoveAsync(Arg.Is<MealPlanRemovalTarget>(x=>x.StudentUid==123&&x.TermId==269&&x.AssignmentId==10&&x.MealPlanId==20),"review","Reviewed assignment and charges");await _plans.DidNotReceiveWithAnyArgs().UpdateHousingStatusAsync(default!,default,default);await _plans.DidNotReceiveWithAnyArgs().AssignAsync(default!,default);}
    [Fact]public async Task Missing_confirmation_is_rejected_before_removal()
    {var input=Input();input.Confirmed=false;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(input));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public async Task Another_terms_assignment_cannot_be_removed()
    {var input=Input();input.AssignmentId=11;input.MealPlanId=21;await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(input));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public async Task Disabled_feature_is_rejected_before_removal()
    {_removal.IsEnabled.Returns(false);await Assert.ThrowsAsync<UserFriendlyException>(()=>_service.RemoveAsync(Input()));await _removal.DidNotReceiveWithAnyArgs().RemoveAsync(default!,default,default,default);}
    [Fact]public void Removal_requires_its_own_permission()
    {Assert.Contains(typeof(AdminMealPlanAppService).GetMethod(nameof(AdminMealPlanAppService.RemoveAsync))!.GetCustomAttributes<AuthorizeAttribute>(),x=>x.Policy==CampusFlowPermissions.Admin.RemoveStudentMealPlan);}
}
