using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Housing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using Xunit;
using IdentityUser=Volo.Abp.Identity.IdentityUser;

namespace CampusFlow.Web.Tests.Housing;

public class HousingRecoveryJobTests : CampusFlowWebTestBase
{
    private sealed record Fixture(Guid Tenant,Guid User,Guid Draft,string Plan,ClaimsPrincipal Principal,
        RecoverHousingDraftInput Input,HousingRecoveryDispatcher Dispatcher,HousingRecoveryJob Job,IHousingWorkspaceAppService Workspace);
    private async Task<Fixture> Setup(bool fail=false,bool permission=true,bool billing=false,bool apply=false,bool partial=false)
    {
        var tenant=GetRequiredService<ICurrentTenant>();var units=GetRequiredService<IUnitOfWorkManager>();
        var actors=GetRequiredService<IRepository<IdentityUser,Guid>>();var drafts=GetRequiredService<IRepository<HousingAssignmentDraft,Guid>>();
        var actorId=Guid.NewGuid();var university=Guid.NewGuid();ClaimsPrincipal principal;
        using(tenant.Change(null))
        using(var unit=units.Begin(requiresNew:true,isTransactional:true))
        {
            var user=new IdentityUser(actorId,"housing-"+actorId.ToString("N"),actorId.ToString("N")+"@example.test");
            await actors.InsertAsync(user,autoSave:true);
            principal=await GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
            await unit.CompleteAsync();
        }
        var rooms=Enumerable.Range(1,20).Select(i=>new HousingWorkspaceRoom{Id=i,Name=i.ToString(),Building="Commuter",Capacity=10}).ToList();
        foreach(var id in Enumerable.Range(1,113))rooms[(id-1)%20].Occupants.Add(new(){StudentUid=id,Name="Test "+id,Rate="Standard"});
        var plan=JsonSerializer.Serialize(rooms);HousingAssignmentDraft draft;
        using(tenant.Change(university,"nelson"))
        using(var unit=units.Begin(requiresNew:true,isTransactional:true))
        {
            draft=new(Guid.NewGuid(),university,2,apply?JsonSerializer.Serialize(rooms.Select(r=>new HousingWorkspaceRoom{Id=r.Id,Name=r.Name,Building=r.Building,Capacity=r.Capacity})):"[]",DateTime.UtcNow);draft.Save(plan);
            new HousingApplyProgress{RunId=Guid.NewGuid(),Status=apply?"Completed":"Needs review",StartedAt=DateTime.UtcNow.AddHours(-1),FinishedAt=DateTime.UtcNow,BeforeJson=draft.BaselineJson,DesiredJson=plan}.Save(draft);
            await drafts.InsertAsync(draft,autoSave:true);await unit.CompleteAsync();
        }
        var input=new RecoverHousingDraftInput{PeriodId=2,Revision=draft.ConcurrencyStamp,RunId=HousingApplyProgress.Read(draft)!.RunId,
            Mode=partial?"partial":billing?"billing":"commuter",RateRevision=billing?"reviewed-rates":"",RecoveryRevision="reviewed-live-snapshot",Confirmed=true,Reason="Reviewed the retained plan"};
        var workspace=Substitute.For<IHousingWorkspaceAppService>();
        workspace.GetAsync(2).Returns(new HousingWorkspaceState{Id=draft.Id,PeriodId=2,Rooms=rooms,ApplyStatus="Needs review"});
        async Task<HousingWorkspaceState> Complete(NSubstitute.Core.CallInfo c)
        {
            Assert.Equal(actorId,GetRequiredService<ICurrentUser>().Id);Assert.Equal(university,tenant.Id);
            Assert.NotNull(HousingRecoveryDispatch.CurrentExecutionId);
            if(!apply)Assert.Equal("reviewed-live-snapshot",c.Arg<RecoverHousingDraftInput>().RecoveryRevision);
            using var unit=units.Begin(requiresNew:true,isTransactional:true);var row=await drafts.GetAsync(draft.Id);
            Assert.Equal(row.ConcurrencyStamp,apply?c.Arg<ApplyHousingDraftInput>().Revision:c.Arg<RecoverHousingDraftInput>().Revision);Assert.Equal(plan,row.RoomsJson);
            var progress=HousingApplyProgress.Read(row)!;progress.Status=fail?"Needs review":"Completed";progress.FinishedAt=DateTime.UtcNow;progress.Save(row);
            if(!fail)row.Refresh(plan,DateTime.UtcNow);
            await drafts.UpdateAsync(row,autoSave:true);await unit.CompleteAsync();
            if(fail)throw new UserFriendlyException("Assignment outcome needs review. The saved plan is retained.");
            return new HousingWorkspaceState{Id=row.Id,PeriodId=2,ApplyStatus="Completed",Rooms=rooms};
        }
        if(apply)workspace.ApplyAssignmentsAsync(Arg.Any<ApplyHousingDraftInput>()).Returns(Complete);
        else if(partial)workspace.ResumePartialAssignmentsAsync(Arg.Any<RecoverHousingDraftInput>()).Returns(Complete);
        else if(billing)workspace.ResumeBillingReconciliationAsync(Arg.Any<RecoverHousingDraftInput>()).Returns(Complete);
        else workspace.ResumeCommuterAssignmentsAsync(Arg.Any<RecoverHousingDraftInput>()).Returns(Complete);
        var store=Substitute.For<ITenantStore>();store.FindAsync(university).Returns(new TenantConfiguration(university,"nelson"));
        var auth=Substitute.For<IAuthorizationService>();auth.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(),Arg.Any<object?>(),CampusFlowPermissions.Admin.HousingAssignments)
            .Returns(permission?AuthorizationResult.Success():AuthorizationResult.Failed());
        var dispatcher=new HousingRecoveryDispatcher(GetRequiredService<IBackgroundJobManager>(),drafts,tenant,GetRequiredService<ICurrentUser>(),
            GetRequiredService<IClock>(),units,workspace,Substitute.For<IElementsHousingAssignmentWriter>());
        var job=new HousingRecoveryJob(drafts,tenant,store,GetRequiredService<IdentityUserManager>(),GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>(),
            GetRequiredService<ICurrentPrincipalAccessor>(),auth,units,GetRequiredService<IClock>(),workspace,NullLogger<HousingRecoveryJob>.Instance);
        return new(university,actorId,draft.Id,plan,principal,input,dispatcher,job,workspace);
    }
    private async Task<HousingRecoveryJobArgs> StoredArgs(string id)
    {
        using var unit=GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew:true,isTransactional:false);
        var row=await GetRequiredService<IBackgroundJobStore>().FindAsync(Guid.Parse(id));Assert.NotNull(row);
        return GetRequiredService<IJsonSerializer>().Deserialize<HousingRecoveryJobArgs>(row!.JobArgs);
    }
    private async Task<HousingAssignmentDraft> Draft(Fixture f)
    {
        using var unit=GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew:true,isTransactional:false);
        return await GetRequiredService<IRepository<HousingAssignmentDraft,Guid>>().GetAsync(f.Draft);
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task A_durable_job_is_queued_without_running_assignments_in_the_request_then_completes_as_the_authorized_actor(bool billing)
    {
        var f=await Setup(billing:billing);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueAsync(f.Input);await f.Workspace.DidNotReceiveWithAnyArgs().ResumeCommuterAssignmentsAsync(default!);
        var args=await StoredArgs(ticket.JobId);Assert.Equal(f.User,args.UserId);Assert.Equal(f.Tenant,args.TenantId);Assert.Equal(f.Plan,(await Draft(f)).RoomsJson);
        await f.Job.ExecuteAsync(args);var after=await Draft(f);Assert.Equal(f.Plan,after.RoomsJson);Assert.Equal("Completed",HousingRecoveryDispatch.Read(after)!.Status);
        Assert.Equal(f.User,HousingRecoveryDispatch.Read(after)!.UserId);Assert.Equal(f.User,GetRequiredService<ICurrentUser>().Id);Assert.Null(HousingRecoveryDispatch.CurrentExecutionId);
        Assert.Equal(billing?"billing":"commuter",ticket.Mode);
        if(billing){await f.Workspace.Received(1).ResumeBillingReconciliationAsync(Arg.Is<RecoverHousingDraftInput>(x=>x.Mode=="billing"&&x.RateRevision=="reviewed-rates"));await f.Workspace.DidNotReceiveWithAnyArgs().ResumeCommuterAssignmentsAsync(default!);}
        else await f.Workspace.Received(1).ResumeCommuterAssignmentsAsync(Arg.Any<RecoverHousingDraftInput>());
    }
    [Fact]public async Task An_uncertain_job_retains_the_plan_and_a_delivery_retry_does_not_repeat_assignments()
    {
        var f=await Setup(fail:true);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueAsync(f.Input);await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        var row=await Draft(f);Assert.Equal(f.Plan,row.RoomsJson);Assert.Equal("Failed",HousingRecoveryDispatch.Read(row)!.Status);
        Assert.Contains("retained",HousingRecoveryDispatch.Read(row)!.Message!);await f.Workspace.Received(1).ResumeCommuterAssignmentsAsync(Arg.Any<RecoverHousingDraftInput>());
        await f.Workspace.DidNotReceiveWithAnyArgs().RefreshAsync(default,default);await f.Workspace.DidNotReceiveWithAnyArgs().SaveAsync(default!);
    }
    [Fact]public async Task An_uncertain_billing_job_is_not_retried_or_sent_to_commuter_recovery()
    {
        var f=await Setup(fail:true,billing:true);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueAsync(f.Input);await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        Assert.Equal("Failed",HousingRecoveryDispatch.Read(await Draft(f))!.Status);
        await f.Workspace.Received(1).ResumeBillingReconciliationAsync(Arg.Any<RecoverHousingDraftInput>());
        await f.Workspace.DidNotReceiveWithAnyArgs().ResumeCommuterAssignmentsAsync(default!);
        Assert.Equal(f.Plan,(await Draft(f)).RoomsJson);
    }
    [Fact]public async Task Permission_is_rechecked_when_the_job_starts()
    {
        var f=await Setup(permission:false);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueAsync(f.Input);await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        Assert.Equal(f.Plan,(await Draft(f)).RoomsJson);Assert.Null(HousingRecoveryDispatch.Read(await Draft(f)));
        await f.Workspace.DidNotReceiveWithAnyArgs().ResumeCommuterAssignmentsAsync(default!);
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task Normal_apply_is_queued_once_and_never_replayed_after_a_failure_or_completion(bool fail)
    {
        var f=await Setup(apply:true,fail:fail);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var input=new ApplyHousingDraftInput{PeriodId=2,Revision=f.Input.Revision,Confirmed=true};
        var ticket=await f.Dispatcher.QueueApplyAsync(input);Assert.Equal("apply",ticket.Mode);Assert.Equal("Queued",HousingRecoveryDispatch.Read(await Draft(f))!.Status);
        await f.Workspace.DidNotReceiveWithAnyArgs().ApplyAssignmentsAsync(default!);
        await Assert.ThrowsAsync<UserFriendlyException>(()=>f.Dispatcher.QueueApplyAsync(input));
        await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        await f.Workspace.Received(1).ApplyAssignmentsAsync(Arg.Any<ApplyHousingDraftInput>());
        Assert.Equal(fail?"Failed":"Completed",HousingRecoveryDispatch.Read(await Draft(f))!.Status);Assert.Equal(f.Plan,(await Draft(f)).RoomsJson);
    }
    [Fact]public async Task Normal_apply_rechecks_permission_and_releases_its_queue_without_writing_rooms()
    {
        var f=await Setup(apply:true,permission:false);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueApplyAsync(new(){PeriodId=2,Revision=f.Input.Revision,Confirmed=true});await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        await f.Workspace.DidNotReceiveWithAnyArgs().ApplyAssignmentsAsync(default!);Assert.Equal("Failed",HousingRecoveryDispatch.Read(await Draft(f))!.Status);Assert.Equal(f.Plan,(await Draft(f)).RoomsJson);
    }
    [Fact]public async Task Partial_recovery_uses_the_reviewed_partial_action_and_never_the_commuter_action()
    {
        var f=await Setup(partial:true);using var tenant=GetRequiredService<ICurrentTenant>().Change(f.Tenant,"nelson");using var actor=GetRequiredService<ICurrentPrincipalAccessor>().Change(f.Principal);
        var ticket=await f.Dispatcher.QueueAsync(f.Input);await f.Job.ExecuteAsync(await StoredArgs(ticket.JobId));
        await f.Workspace.Received(1).ResumePartialAssignmentsAsync(Arg.Any<RecoverHousingDraftInput>());await f.Workspace.DidNotReceiveWithAnyArgs().ResumeCommuterAssignmentsAsync(default!);Assert.Equal("Completed",HousingRecoveryDispatch.Read(await Draft(f))!.Status);
    }
    [Fact]public void Production_dependency_registration_includes_the_dispatcher_worker_and_actor_factory()
    {
        Assert.NotNull(GetRequiredService<IHousingRecoveryDispatcher>());Assert.NotNull(GetRequiredService<HousingRecoveryJob>());
        Assert.NotNull(GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>());
    }
}
