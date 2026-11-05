using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace CampusFlow.BillApprovals;

public class BillApprovalTermConfiguration : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string ExternalTermId { get; private set; } = null!;
    public string TermCode { get; private set; } = null!;
    public string TermName { get; private set; } = null!;
    public DateTime OpensAt { get; private set; }
    public DateTime ClosesAt { get; private set; }
    public bool IsEnabled { get; private set; }
    public Guid AgreementTemplateId { get; private set; }
    public Guid PaymentPlanPolicyId { get; private set; }

    protected BillApprovalTermConfiguration() { }

    public BillApprovalTermConfiguration(Guid id, Guid? tenantId, string externalTermId,
        string termCode, string termName, DateTime opensAt, DateTime closesAt, bool isEnabled,
        Guid agreementTemplateId, Guid paymentPlanPolicyId) : base(id)
    {
        TenantId = tenantId;
        ExternalTermId = externalTermId;
        TermCode = termCode;
        TermName = termName;
        Update(opensAt, closesAt, isEnabled, agreementTemplateId, paymentPlanPolicyId);
    }

    public void Update(DateTime opensAt, DateTime closesAt, bool isEnabled,
        Guid agreementTemplateId, Guid paymentPlanPolicyId)
    {
        if (closesAt <= opensAt) throw new ArgumentException("Bill Approval must close after it opens.");
        OpensAt = opensAt;
        ClosesAt = closesAt;
        IsEnabled = isEnabled;
        AgreementTemplateId = agreementTemplateId;
        PaymentPlanPolicyId = paymentPlanPolicyId;
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool TestAccessEnabled => this.GetProperty<bool>("BillApprovalTestAccessEnabled");

    public IReadOnlyList<string> GetTestStudentIds()
    {
        try { return JsonSerializer.Deserialize<string[]>(this.GetProperty<string>("BillApprovalTestStudentIds", "[]") ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    public void ConfigureTestAccess(bool enabled, IEnumerable<string> studentIds)
    {
        ArgumentNullException.ThrowIfNull(studentIds);
        var ids = studentIds.Select(x => x?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > 100 || ids.Any(x => x.Length is < 1 or > 64 || !x.All(char.IsAsciiDigit)))
            throw new ArgumentException("Enter up to 100 numeric Student IDs, each at most 64 digits.");
        if (enabled && ids.Length == 0)
            throw new ArgumentException("Test access requires at least one Student ID.");
        this.SetProperty("BillApprovalTestAccessEnabled", enabled);
        this.SetProperty("BillApprovalTestStudentIds", JsonSerializer.Serialize(ids.OrderBy(x => x).ToArray()));
    }

    public bool IsOpenForStudent(DateTime at, string studentId) => IsOpen(at) ||
        (TestAccessEnabled && !string.IsNullOrWhiteSpace(studentId) && GetTestStudentIds().Contains(studentId.Trim(), StringComparer.Ordinal));

    public string Availability(DateTime at, string studentId) => IsOpenForStudent(at, studentId) ? "open"
        : at > ClosesAt ? "closed" : IsEnabled && at < OpensAt ? "upcoming" : "unavailable";

    public bool IsOpen(DateTime at) => IsEnabled && OpensAt <= at && ClosesAt >= at;
}
