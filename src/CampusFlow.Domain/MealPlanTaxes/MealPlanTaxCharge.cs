using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.MealPlanTaxes;

public sealed class MealPlanTaxCharge : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public int StudentUid { get; private set; }
    public int TermId { get; private set; }
    public int AssignmentId { get; private set; }
    public int MealPlanId { get; private set; }
    public decimal BaseAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public string Reference { get; private set; } = null!;
    public string State { get; private set; } = "Attempting";
    public string? ExternalEntryId { get; private set; }
    protected MealPlanTaxCharge() { }
    public MealPlanTaxCharge(Guid id, Guid tenant, MealPlanTaxTarget target) : base(id)
    {
        if(tenant==Guid.Empty)throw new ArgumentException("A tenant is required.");
        target.Validate();TenantId=tenant;StudentUid=target.StudentUid;TermId=target.TermId;
        AssignmentId=target.AssignmentId;MealPlanId=target.MealPlanId;BaseAmount=target.BaseAmount;
        TaxAmount=target.TaxAmount;Reference=target.Reference;
    }
    public bool Matches(MealPlanTaxTarget target) => StudentUid==target.StudentUid && TermId==target.TermId &&
        AssignmentId==target.AssignmentId && MealPlanId==target.MealPlanId && BaseAmount==target.BaseAmount &&
        TaxAmount==target.TaxAmount && Reference==target.Reference;
    public void Verify(string entryId)
    {
        if(string.IsNullOrWhiteSpace(entryId)||entryId.Length>80)throw new ArgumentException("A verified entry is required.");
        ExternalEntryId=entryId;State="Verified";
    }
    public void Uncertain() { if(State!="Verified") State="Needs review"; }
}
