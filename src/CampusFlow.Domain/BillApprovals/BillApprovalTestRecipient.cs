using System;
using System.Net.Mail;
using System.Text.Json;
using Volo.Abp.Data;
namespace CampusFlow.BillApprovals;

// This is captured from the server's signed-in actor, never from a posted recipient field.
public sealed record BillApprovalTestRecipient(Guid ApprovalId, Guid TenantId, Guid ActorUserId, string Email, DateTime CapturedAt)
{
    public const string PropertyName = "BillApprovalNonprodRecipientV1";
    public static bool IsSchoolAddress(string? value) => MailAddress.TryCreate(value, out var address) &&
        string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(address.Host, "nelson.edu", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(address.Host, "my.nelson.edu", StringComparison.OrdinalIgnoreCase));

    public static void Capture(BillApproval approval, Guid actorUserId, string? email, DateTime now)
    {
        if (!approval.TenantId.HasValue || actorUserId == Guid.Empty || !IsSchoolAddress(email))
            throw new BillCompletionPreparationException("A signed-in Nelson account with a school email address is required for nonprod delivery.");
        var existing = Read(approval);
        if (existing is not null)
        {
            if (existing.ActorUserId != actorUserId || !string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase))
                throw new BillCompletionPreparationException("This approval already has a nonprod test recipient. Retries retain the original recipient.");
            return;
        }
        approval.SetProperty(PropertyName, JsonSerializer.Serialize(new BillApprovalTestRecipient(
            approval.Id, approval.TenantId.Value, actorUserId, email!, now)));
    }
    public static BillApprovalTestRecipient? Read(BillApproval approval)
    {
        var json = approval.GetProperty<string>(PropertyName);
        if (json is null) return null;
        BillApprovalTestRecipient? value;
        try { value = JsonSerializer.Deserialize<BillApprovalTestRecipient>(json); }
        catch (JsonException) { throw new BillCompletionPreparationException("The nonprod recipient record needs administrative review."); }
        if (value is null || value.ApprovalId != approval.Id || value.TenantId != approval.TenantId ||
            value.ActorUserId == Guid.Empty || !IsSchoolAddress(value.Email))
            throw new BillCompletionPreparationException("The nonprod recipient does not match this approval.");
        return value;
    }
    public static BillApprovalTestRecipient Require(BillApproval approval) => Read(approval) ??
        throw new BillCompletionPreparationException("This older approval has no signed-in nonprod test recipient. An administrator must explicitly prepare it for a test retry.");
}
