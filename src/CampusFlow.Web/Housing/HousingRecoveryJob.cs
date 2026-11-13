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
}
public sealed record QueuedHousingRecovery(string JobId,Guid DispatchId,HousingWorkspaceState State);
public interface IHousingRecoveryDispatcher
{
    Task<QueuedHousingRecovery> QueueAsync(RecoverHousingDraftInput input);
}

public sealed class HousingRecoveryDispatcher(IBackgroundJobManager jobs,IRepository<HousingAssignmentDraft,Guid> drafts,
    ICurrentTenant tenant,ICurrentUser user,IClock clock,IUnitOfWorkManager units,
    IHousingWorkspaceAppService workspace,IElementsHousingAssignmentWriter writer) : IHousingRecoveryDispatcher,ITransientDependency
{
    public async Task<QueuedHousingRecovery> QueueAsync(RecoverHousingDraftInput input)
    {
        writer.EnsureWriteEnabled();
        if(tenant.Id is not Guid tenantId || user.Id is not Guid userId || !input.Confirmed || input.PeriodId<=0 ||
            input.RunId==Guid.Empty || string.IsNullOrWhiteSpace(input.RecoveryRevision) || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length>500)
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
        return new(id,dispatch,await workspace.GetAsync(input.PeriodId)??throw new UserFriendlyException("The saved plan could not be loaded."));
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
        if(args.TenantId==Guid.Empty || args.UserId==Guid.Empty || args.DispatchId==Guid.Empty || args.Input.PeriodId<=0)return;
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
                var draft=await drafts.FindAsync(x=>x.TenantId==args.TenantId && x.PeriodId==args.Input.PeriodId);
                var progress=draft is null?null:HousingApplyProgress.Read(draft);
                if(draft is null || draft.ConcurrencyStamp!=args.Input.Revision || progress?.RunId!=args.Input.RunId ||
                    !progress.CanRecover(clock.Now) || HousingRecoveryDispatch.Read(draft)?.IsActive(draft,clock.Now)==true)return;
                new HousingRecoveryDispatch{Id=args.DispatchId,UserId=args.UserId,StartedAt=clock.Now}.Save(draft);
                await drafts.UpdateAsync(draft,autoSave:true);await claim.CompleteAsync();
                // Only queue metadata changed. The reviewed hash still binds the complete
                // saved plan and live snapshot; Apply's claim checks this new revision.
                args.Input.Revision=draft.ConcurrencyStamp;claimed=true;
            }
            using(HousingRecoveryDispatch.EnterExecution(args.DispatchId))
                await workspace.ResumeCommuterAssignmentsAsync(args.Input);
            await Finish(args,"Completed","The saved commuter plan was applied and verified. Existing billing was preserved.");
        }
        catch(Exception exception)
        {
            logger.LogWarning(exception,"Housing recovery dispatch {DispatchId} stopped; review live assignments before another attempt.",args.DispatchId);
            if(claimed)
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
        using var unit=units.Begin(requiresNew:true,isTransactional:true);
        var draft=await drafts.FindAsync(x=>x.TenantId==args.TenantId && x.PeriodId==args.Input.PeriodId);
        var dispatch=draft is null?null:HousingRecoveryDispatch.Read(draft);
        if(dispatch?.Id!=args.DispatchId)return;
        if(status=="Completed" && HousingApplyProgress.Read(draft!)?.Status!="Completed")
            throw new UserFriendlyException("Recovery returned without a verified completion. The saved plan is retained for review.");
        dispatch.Status=status;dispatch.Message=message;dispatch.FinishedAt=clock.Now;dispatch.Save(draft!);
        await drafts.UpdateAsync(draft!,autoSave:true);await unit.CompleteAsync();
    }
}
