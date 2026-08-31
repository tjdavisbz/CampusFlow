using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.Payments;

public enum PayflowPaymentStatus { Created, Pending, Approved, Declined, Cancelled, Failed }
public enum ElementsPaymentPostingStatus { NotRequired, Pending, Posted, Failed }

public class PayflowPayment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string ExternalStudentId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string SecureTokenId { get; private set; } = null!;
    public string? SecureToken { get; private set; }
    public string? PayflowReference { get; private set; }
    public PayflowPaymentStatus Status { get; private set; }
    public int? GatewayResult { get; private set; }
    public string? GatewayMessage { get; private set; }
    public bool IsTest { get; private set; }
    public ElementsPaymentPostingStatus ElementsPostingStatus { get; private set; }
    public int ElementsPostingAttempts { get; private set; }
    public int? ElementsBatchMasterId { get; private set; }
    public int? ElementsBillingBatchId { get; private set; }
    public string? ElementsPostingError { get; private set; }
    public DateTime? ElementsPostedAt { get; private set; }

    protected PayflowPayment() { }

    public PayflowPayment(Guid id, Guid? tenantId, Guid userId, Guid studentProfileId,
        string externalStudentId, decimal amount, string currency, string secureTokenId, bool isTest) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        StudentProfileId = studentProfileId;
        ExternalStudentId = externalStudentId;
        Amount = amount;
        Currency = currency;
        SecureTokenId = secureTokenId;
        IsTest = isTest;
        Status = PayflowPaymentStatus.Created;
    }

    public void MarkPending(string secureToken) { SecureToken = secureToken; Status = PayflowPaymentStatus.Pending; }
    public void Complete(bool approved, int result, string? message, string? reference)
    {
        GatewayResult = result; GatewayMessage = Trim(message); PayflowReference = Trim(reference);
        Status = approved ? PayflowPaymentStatus.Approved : PayflowPaymentStatus.Declined;
        ElementsPostingStatus = approved ? ElementsPaymentPostingStatus.Pending : ElementsPaymentPostingStatus.NotRequired;
    }
    public void MarkElementsPosted(int batchMasterId, int billingBatchId)
    {
        ElementsPostingAttempts++;
        ElementsBatchMasterId = batchMasterId;
        ElementsBillingBatchId = billingBatchId;
        ElementsPostingError = null;
        ElementsPostedAt = DateTime.UtcNow;
        ElementsPostingStatus = ElementsPaymentPostingStatus.Posted;
    }
    public void MarkElementsPostingFailed(string? error)
    {
        ElementsPostingAttempts++;
        ElementsPostingError = Trim(error);
        ElementsPostingStatus = ElementsPaymentPostingStatus.Failed;
    }
    public void Cancel() => Status = PayflowPaymentStatus.Cancelled;
    public void Fail(int? result, string? message) { GatewayResult = result; GatewayMessage = Trim(message); Status = PayflowPaymentStatus.Failed; }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 1000)];
}
