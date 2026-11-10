using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace CampusFlow.MealPlanTaxes;
public sealed record MealPlanTaxTarget(int StudentUid,int TermId,int AssignmentId,int MealPlanId,decimal BaseAmount)
{
    public decimal TaxAmount => MealPlanTaxRules.Amount(BaseAmount);
    public string Reference => $"CFMT-{TermId}-{StudentUid}-{AssignmentId}";
    public void Validate()
    {
        if(StudentUid<=0||TermId<=0||AssignmentId<=0||MealPlanId<=0||BaseAmount<=0||BaseAmount>1000000m||decimal.Round(BaseAmount,2)!=BaseAmount)
            throw new InvalidOperationException("The reviewed meal-plan tax identity or amount is invalid.");
    }
}
public static class MealPlanTaxRules
{
    public const decimal Rate = .0825m;
    public static decimal Amount(decimal baseAmount)
    {
        if(baseAmount<0||baseAmount>1000000m)throw new ArgumentOutOfRangeException(nameof(baseAmount));
        return decimal.Round(baseAmount*Rate,2,MidpointRounding.AwayFromZero);
    }
}
public sealed record MealPlanTaxClaim(Guid Id,bool MaySend);
public interface IMealPlanTaxChargeStore
{
    Task<MealPlanTaxClaim> ClaimAsync(MealPlanTaxTarget target,CancellationToken ct=default);
    Task VerifyAsync(Guid id,string externalEntryId,CancellationToken ct=default);
    Task MarkUncertainAsync(Guid id,CancellationToken ct=default);
}
public interface IElementsMealPlanTaxGateway
{
    Task<IReadOnlyList<MealPlanTaxTarget>> TargetsAsync(int studentUid,int termId,CancellationToken ct=default);
    Task<string?> FindAsync(MealPlanTaxTarget target,CancellationToken ct=default);
    Task CreateAsync(MealPlanTaxTarget target,CancellationToken ct=default);
    Task ConfirmReportingAsync(MealPlanTaxTarget target,CancellationToken ct=default);
}
public interface IMealPlanTaxCoordinator
{
    Task EnsureForBillReviewAsync(string studentUid,string termId,CancellationToken ct=default);
}

public sealed record MealPlanTaxDestination(int BatchId,int DocumentId,int ArTypeId);
public sealed record MealPlanTaxReportedCharge(string? PostedId,string? BatchId);
public interface IElementsMealPlanTaxReadModel
{
    Task<bool> IsInScopeAsync(int termId,CancellationToken ct=default);
    Task<MealPlanTaxDestination> DestinationAsync(CancellationToken ct=default);
    Task<MealPlanTaxReportedCharge> ReadAsync(MealPlanTaxTarget target,MealPlanTaxDestination destination,CancellationToken ct=default);
}
