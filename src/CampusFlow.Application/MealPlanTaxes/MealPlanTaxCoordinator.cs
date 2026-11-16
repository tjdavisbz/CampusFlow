using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.MealPlanTaxes;
public sealed class MealPlanTaxCoordinator(IElementsMealPlanTaxGateway gateway,IMealPlanTaxChargeStore store,
    ICurrentTenant tenant,ILogger<MealPlanTaxCoordinator> logger,IConfiguration configuration) : IMealPlanTaxCoordinator, ITransientDependency
{
    public Task EnsureForSelectionAsync(string studentUid,string termId,CancellationToken ct=default) =>
        configuration.GetValue<bool>("MealPlanTax:Enabled") ? EnsureForBillReviewAsync(studentUid,termId,ct) : Task.CompletedTask;
    public async Task EnsureForBillReviewAsync(string studentUid,string termId,CancellationToken ct=default)
    {
        if(!tenant.Id.HasValue||!string.Equals(tenant.Name,"nelson",StringComparison.OrdinalIgnoreCase)||
            !int.TryParse(studentUid,out var student)||student<=0||!int.TryParse(termId,out var term)||term<=0)
            throw new UserFriendlyException("Meal-plan tax requires the matching university, student and term.");
        var targets=await gateway.TargetsAsync(student,term,ct);
        foreach(var target in targets)
        {
            target.Validate();
            if(target.StudentUid!=student||target.TermId!=term)throw new InvalidOperationException("Meal-plan tax scope changed.");
            // Complete read-only authentication/conflict checks before recording any
            // send attempt. A temporary read failure must remain safe to retry.
            var existing=await gateway.FindAsync(target,ct);
            var claim=await store.ClaimAsync(target,ct);
            try
            {
                if(existing is null && !claim.MaySend)existing=await gateway.FindAsync(target,ct);
                if(existing is null)
                {
                    if(!claim.MaySend)throw new InvalidOperationException("A prior tax attempt needs review; it will not be resent.");
                    try { await gateway.CreateAsync(target,ct); }
                    catch(MealPlanTaxWriteUnconfirmedException)
                    {
                        // Only a send-stage failure is eligible for read-only recovery.
                        // Preflight/ownership failures must still stop the operation.
                        for(var attempt=0;attempt<4;attempt++)
                        {
                            existing=await gateway.FindAsync(target,ct);
                            if(existing is not null)break;
                            if(attempt<3)await Task.Delay(1000,ct);
                        }
                        if(existing is null)throw;
                    }
                    existing??=await gateway.FindAsync(target,ct)
                        ?? throw new InvalidOperationException("The tax charge could not be confirmed.");
                }
                await gateway.ConfirmReportingAsync(target,ct);
                await store.VerifyAsync(claim.Id,existing,ct);
            }
            catch(Exception exception)
            {
                // Persist uncertainty even if the HTTP request was cancelled. A later
                // review may confirm the same reference, but may never resend it blindly.
                logger.LogWarning(exception,"Meal-plan tax attempt {AttemptId} needs billing review for assignment {AssignmentId}.",claim.Id,target.AssignmentId);
                await store.MarkUncertainAsync(claim.Id,CancellationToken.None);
                throw new UserFriendlyException("Your meal-plan sales tax could not be confirmed. Contact Student Billing before approving your bill; a charge may already exist.");
            }
        }
    }
}
