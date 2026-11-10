using System;using System.Linq;using System.Threading;using System.Threading.Tasks;using CampusFlow.Housing;using CampusFlow.StudentInformationSystems;using Microsoft.Extensions.Logging.Abstractions;using NSubstitute;using Volo.Abp;using Volo.Abp.DependencyInjection;using Volo.Abp.MultiTenancy;using Volo.Abp.Users;using Xunit;
namespace CampusFlow.Application.Tests.Housing;
public class AdminMealPlanAssignmentTests
{
    static(AdminMealPlanAppService app,IStudentInformationSystemMealPlanService plans,IStudentInformationSystemMealPlanRemovalService reader) Setup()
    {
        var student=Substitute.For<IStudentInformationSystemStudentLookup>();student.Provider.Returns(StudentInformationSystemProvider.ThesisElements);student.FindByExternalStudentIdAsync("123",Arg.Any<CancellationToken>()).Returns(new StudentInformationSystemStudent(StudentInformationSystemProvider.ThesisElements,"123","test","student@example.test","Test",null,"Student"));
        var plans=Substitute.For<IStudentInformationSystemMealPlanService>();plans.Provider.Returns(StudentInformationSystemProvider.ThesisElements);plans.GetContextAsync("123",Arg.Any<int?>(),Arg.Any<CancellationToken>()).Returns(new StudentMealPlanContext("Commuter",new[]{new StudentMealPlanCatalogItem(20,"Plan","Plan",100,new(2027,1,11),new(2027,4,30))},new[]{new StudentMealPlanTerm(269,"Spring 2027",new(2027,1,11),new(2027,4,30))},269,new[]{new StudentHousingStatusOption(10,"Commuter")},10));
        var reader=Substitute.For<IStudentInformationSystemMealPlanRemovalService>();var lazy=Substitute.For<IAbpLazyServiceProvider>();var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);var user=Substitute.For<ICurrentUser>();user.Id.Returns(Guid.NewGuid());lazy.LazyGetRequiredService<ICurrentUser>().Returns(user);
        return(new AdminMealPlanAppService(new[]{student},new[]{plans},NullLogger<AdminMealPlanAppService>.Instance,reader){LazyServiceProvider=lazy},plans,reader);
    }
    static AssignStudentMealPlanInput Input()=>new(){ExternalStudentId="123",TermCalendarId=269,HousingStatusId=10,MealPlanId=20};
    [Theory] [InlineData(269,true)] [InlineData(268,false)] [InlineData(0,false)]
    public async Task An_API_success_is_not_reported_as_success_without_the_requested_term_assignment(int returnedTerm,bool succeeds)
    {var(a,p,r)=Setup();r.GetAssignmentsAsync("123",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentMealPlanAssignment>(),returnedTerm==0?Array.Empty<StudentMealPlanAssignment>():new[]{new StudentMealPlanAssignment(40,20,returnedTerm,"Plan",100)});if(succeeds)await a.AssignAsync(Input());else{var e=await Assert.ThrowsAsync<UserFriendlyException>(()=>a.AssignAsync(Input()));Assert.Contains("could not be confirmed",e.Message);}await p.Received(1).AssignAsync("123",20,Arg.Any<CancellationToken>());}
    [Fact] public async Task A_retried_existing_assignment_cannot_create_a_duplicate_or_change_housing()
    {var(a,p,r)=Setup();r.GetAssignmentsAsync("123",Arg.Any<CancellationToken>()).Returns(new[]{new StudentMealPlanAssignment(40,20,269,"Plan",100)});await Assert.ThrowsAsync<UserFriendlyException>(()=>a.AssignAsync(Input()));await p.DidNotReceiveWithAnyArgs().AssignAsync(default!,default,default);await p.DidNotReceiveWithAnyArgs().UpdateHousingStatusAsync(default!,default,default,default);}
    [Fact] public async Task An_unconfirmed_POST_is_not_automatically_retried()
    {var(a,p,r)=Setup();r.GetAssignmentsAsync("123",Arg.Any<CancellationToken>()).Returns(Array.Empty<StudentMealPlanAssignment>());p.AssignAsync("123",20,Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Unknown result")));var e=await Assert.ThrowsAsync<UserFriendlyException>(()=>a.AssignAsync(Input()));Assert.Contains("may already have succeeded",e.Message);await p.Received(1).AssignAsync("123",20,Arg.Any<CancellationToken>());}
    [Fact] public async Task An_unknown_selected_term_cannot_expose_or_assign_other_terms_plans()
    {
        var(a,p,r)=Setup();var current=await p.GetContextAsync("123",269);
        p.GetContextAsync("123",999,Arg.Any<CancellationToken>()).Returns(current with {SelectedTermCalendarId=999});
        await Assert.ThrowsAsync<UserFriendlyException>(()=>a.GetContextAsync("123",999));
        var input=Input();input.TermCalendarId=999;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>a.AssignAsync(input));
        await p.DidNotReceiveWithAnyArgs().UpdateHousingStatusAsync(default!,default,default,default);
        await p.DidNotReceiveWithAnyArgs().AssignAsync(default!,default,default);
    }
}
