using System;
using System.Threading.Tasks;
using CampusFlow.Housing;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using CampusFlow.StudentInformationSystems;
using IdentityUser=Volo.Abp.Identity.IdentityUser;

namespace CampusFlow.Web.Housing;

public sealed class HousingRecoveryJobArgs
{
    public Guid DispatchId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ActorTenantId { get; set; }
    public RecoverHousingDraftInput Input { get; set; } = new();
    public ApplyHousingDraftInput? ApplyInput { get; set; }
}
public sealed record QueuedHousingRecovery(string JobId,Guid DispatchId,HousingWorkspaceState State, string Mode = "commuter");
public interface IHousingRecoveryDispatcher
{
    Task<QueuedHousingRecovery> QueueAsync(RecoverHousingDraftInput input);
    Task<QueuedHousingRecovery> QueueApplyAsync(ApplyHousingDraftInput input);
}

public sealed class HousingRecoveryDispatcher(IBackgroundJobManager jobs,IRepository<HousingAssignmentDraft,Guid> drafts,
    ICurrentTenant tenant,ICurrentUser user,IClock clock,IUnitOfWorkManager units,
    IHousingWorkspaceAppService workspace,IElementsHousingAssignmentWriter writer) : IHousingRecoveryDispatcher,ITransientDependency
{
    public async Task<QueuedHousingRecovery> QueueApplyAsync(ApplyHousingDraftInput input)
    {
        writer.EnsureWriteEnabled();
        if(tenant.Id is not Guid tenantId||user.Id is not Guid userId||!input.Confirmed||input.PeriodId<=0)
            throw new UserFriendlyException("Review and confirm the saved housing changes before applying.");
        using var unit=units.Begin(requiresNew:true,isTransactional:true);
        // Validate the workspace's tenant scope before queuing any work.
        await workspace.GetAsync(input.PeriodId);
        var draft=await drafts.FindAsync(x=>x.TenantId==tenantId&&x.PeriodId==input.PeriodId)
            ??throw new UserFriendlyException("The saved housing plan no longer exists.");
        if(draft.ConcurrencyStamp!=input.Revision||HousingRecoveryDispatch.Read(draft)?.IsActive(draft,clock.Now)==true)
            throw new UserFriendlyException("The saved plan changed or housing work is already queued. Reload its status.");
        try
        {
            HousingApplyProgress.EnsureEditable(draft,clock.Now);
            var before=System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HousingWorkspaceRoom>>(draft.BaselineJson)!;
            var desired=System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HousingWorkspaceRoom>>(draft.RoomsJson)!;
            if(HousingWorkspacePlanner.Compare(before,desired).Count==0)
                throw new UserFriendlyException("There are no room, rate or capacity changes to apply.");
        }
        catch(InvalidOperationException e){throw new UserFriendlyException(e.Message);}
        catch(ArgumentException e){throw new UserFriendlyException(e.Message);}
        var dispatch=Guid.NewGuid();
        new HousingRecoveryDispatch{Id=dispatch,UserId=userId,StartedAt=clock.Now,Status="Queued",Mode="apply"}.Save(draft);
        await drafts.UpdateAsync(draft,autoSave:true);
        input.Revision=draft.ConcurrencyStamp;
        var id=await jobs.EnqueueAsync(new HousingRecoveryJobArgs{DispatchId=dispatch,TenantId=tenantId,UserId=userId,ActorTenantId=user.TenantId,ApplyInput=input});
        await unit.CompleteAsync();
        return new(id,dispatch,await workspace.GetAsync(input.PeriodId)??throw new UserFriendlyException("The saved plan could not be loaded."),"apply");
    }

    public async Task<QueuedHousingRecovery> QueueAsync(RecoverHousingDraftInput input)
    {
        writer.EnsureWriteEnabled();
        if(tenant.Id is not Guid tenantId || user.Id is not Guid userId || !input.Confirmed || input.PeriodId<=0 ||
            input.Mode is not ("commuter" or "billing" or "partial") || input.RunId==Guid.Empty || string.IsNullOrWhiteSpace(input.RecoveryRevision) || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length>500)
            throw new UserFriendlyException("Review recovery, confirm the saved plan and enter a reason before resuming.");
        using var unit=units.Begin(requiresNew:true,isTransactional:true);
        var draft=await drafts.FindAsync(x=>x.TenantId==tenantId && x.PeriodId==input.PeriodId)
            ?? throw new UserFriendlyException("The saved housing plan no longer exists.");
        var progress=HousingApplyProgress.Read(draft);
        if(draft.ConcurrencyStamp!=input.Revision || progress?.RunId!=input.RunId || !progress.CanRecover(clock.Now) ||
            HousingRecoveryDispatch.Read(draft)?.IsActive(draft,clock.Now)==true)
            throw new UserFriendlyException("The saved run changed or recovery is already running. Reload its status before reviewing again.");
        var dispatch=Guid.NewGuid();
        var id=await jobs.EnqueueAsync(new HousingRecoveryJobArgs{DispatchId=dispatch,TenantId=tenantId,UserId=userId,ActorTenantId=user.TenantId,Input=input});
        await unit.CompleteAsync();
        return new(id,dispatch,await workspace.GetAsync(input.PeriodId)??throw new UserFriendlyException("The saved plan could not be loaded."),input.Mode);
    }
}

// Durable ABP jobs survive the browser request. Uncertain work is never automatically replayed.
public sealed class HousingRecoveryJob(IRepository<HousingAssignmentDraft,Guid> drafts,ICurrentTenant tenant,ITenantStore tenants,
    IdentityUserManager users,IUserClaimsPrincipalFactory<IdentityUser> principals,ICurrentPrincipalAccessor actor,
    IAuthorizationService authorization,IUnitOfWorkManager units,IClock clock,IHousingWorkspaceAppService workspace,
    ILogger<HousingRecoveryJob> logger) : AsyncBackgroundJob<HousingRecoveryJobArgs>,ITransientDependency
{
    public override async Task ExecuteAsync(HousingRecoveryJobArgs args)
    {
        var periodId=args.ApplyInput?.PeriodId??args.Input.PeriodId;
        if(args.TenantId==Guid.Empty || args.UserId==Guid.Empty || args.DispatchId==Guid.Empty || periodId<=0)return;
        var university=await tenants.FindAsync(args.TenantId);
        if(!string.Equals(university?.Name,"nelson",StringComparison.OrdinalIgnoreCase))return;
        var claimed=false;
        try
        {
            System.Security.Claims.ClaimsPrincipal principal;
            using(tenant.Change(args.ActorTenantId))
            {
                using var identityUnit=units.Begin(requiresNew:true,isTransactional:false);
                var user=await users.FindByIdAsync(args.UserId.ToString());
                if(user is null || !user.IsActive || user.TenantId!=args.ActorTenantId)return;
                principal=await principals.CreateAsync(user);
                await identityUnit.CompleteAsync();
            }
            using var universityScope=tenant.Change(args.TenantId,university!.Name);
            using var actorScope=actor.Change(principal);
            using(var permissionUnit=units.Begin(requiresNew:true,isTransactional:false))
            {
                if(!(await authorization.AuthorizeAsync(principal,null,CampusFlowPermissions.Admin.HousingAssignments)).Succeeded)
                    throw new Volo.Abp.Authorization.AbpAuthorizationException("Housing recovery permission is no longer available.");
                await permissionUnit.CompleteAsync();
            }
            using(var claim=units.Begin(requiresNew:true,isTransactional:true))
            {
                var draft=await drafts.FindAsync(x=>x.TenantId==args.TenantId && x.PeriodId==periodId);
                var progress=draft is null?null:HousingApplyProgress.Read(draft);
                if(args.ApplyInput is { } apply)
                {
                    var queued=draft is null?null:HousingRecoveryDispatch.Read(draft);
                    if(draft is null||draft.ConcurrencyStamp!=apply.Revision||queued?.Id!=args.DispatchId||queued.Status!="Queued"||!queued.IsActive(draft,clock.Now))return;
                    HousingApplyProgress.EnsureEditable(draft,clock.Now);
                }
                else if(draft is null || draft.ConcurrencyStamp!=args.Input.Revision || progress?.RunId!=args.Input.RunId ||
                    !progress.CanRecover(clock.Now) || HousingRecoveryDispatch.Read(draft)?.IsActive(draft,clock.Now)==true)return;
                new HousingRecoveryDispatch{Id=args.DispatchId,UserId=args.UserId,StartedAt=clock.Now,Mode=args.ApplyInput is null?"recovery":"apply"}.Save(draft);
                await drafts.UpdateAsync(draft,autoSave:true);await claim.CompleteAsync();
                // Only queue metadata changed. The reviewed hash still binds the complete
                // saved plan and live snapshot; Apply's claim checks this new revision.
                if(args.ApplyInput is not null)args.ApplyInput.Revision=draft.ConcurrencyStamp;
                else args.Input.Revision=draft.ConcurrencyStamp;claimed=true;
            }
            using(HousingRecoveryDispatch.EnterExecution(args.DispatchId))
            {
                if(args.ApplyInput is not null)await workspace.ApplyAssignmentsAsync(args.ApplyInput);
                else if(args.Input.Mode=="partial")await workspace.ResumePartialAssignmentsAsync(args.Input);
                else if(args.Input.Mode=="billing") await workspace.ResumeBillingReconciliationAsync(args.Input);
                else if(args.Input.Mode=="commuter") await workspace.ResumeCommuterAssignmentsAsync(args.Input);
                else throw new UserFriendlyException("The recovery mode is invalid.");
            }
            await Finish(args,"Completed",args.ApplyInput is not null||args.Input.Mode=="partial"
                ? "The saved room changes and housing charges completed and were verified."
                : args.Input.Mode=="billing"
                ? "The saved housing charges were reconciled and verified. Completed room changes were preserved."
                : "The saved commuter plan was applied and verified. Existing billing was preserved.");
        }
        catch(Exception exception)
        {
            logger.LogWarning(exception,"Housing recovery dispatch {DispatchId} stopped; review live assignments before another attempt.",args.DispatchId);
            if(claimed||args.ApplyInput is not null)
            {
                try
                {
                    using(tenant.Change(args.TenantId,university!.Name))
                        await Finish(args,"Failed",exception is UserFriendlyException friendly?friendly.Message:
                            "Recovery could not be confirmed. The saved plan is retained. Review live assignments before another attempt.");
                }
                catch(Exception statusError){logger.LogWarning(statusError,"Housing recovery status {DispatchId} could not be saved.",args.DispatchId);}
            }
            // The job completes even after uncertainty. A new reviewed request is required;
            // ABP must not blindly retry an assignment whose response may have been lost.
        }
    }
    private async Task Finish(HousingRecoveryJobArgs args,string status,string message)
    {
        var periodId=args.ApplyInput?.PeriodId??args.Input.PeriodId;
        using var unit=units.Begin(requiresNew:true,isTransactional:true);
        var draft=await drafts.FindAsync(x=>x.TenantId==args.TenantId && x.PeriodId==periodId);
        var dispatch=draft is null?null:HousingRecoveryDispatch.Read(draft);
        if(dispatch?.Id!=args.DispatchId)return;
        if(status=="Completed" && HousingApplyProgress.Read(draft!)?.Status!="Completed")
            throw new UserFriendlyException("Recovery returned without a verified completion. The saved plan is retained for review.");
        dispatch.Status=status;dispatch.Message=message;dispatch.FinishedAt=clock.Now;dispatch.Save(draft!);
        await drafts.UpdateAsync(draft!,autoSave:true);await unit.CompleteAsync();
    }
}
