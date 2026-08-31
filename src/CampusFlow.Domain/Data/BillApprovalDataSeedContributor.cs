using System;
using System.Text.Json;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Data;

public class BillApprovalDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<AgreementTemplate, Guid> _agreements;
    private readonly IRepository<PaymentPlanPolicy, Guid> _policies;
    private readonly IGuidGenerator _guidGenerator;

    public BillApprovalDataSeedContributor(
        IRepository<AgreementTemplate, Guid> agreements,
        IRepository<PaymentPlanPolicy, Guid> policies,
        IGuidGenerator guidGenerator)
    {
        _agreements = agreements;
        _policies = policies;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue) return;

        if (await _agreements.FindAsync(x => x.TenantId == context.TenantId && x.Name == "Student Bill Agreement" && x.Version == 1) is null)
        {
            const string html = """
                <h3>Federal funds</h3>
                <p>I understand that estimated financial aid is a projection of available funds and is not a guarantee of payment. I authorize the University to apply eligible funds to charges on my student account.</p>
                <h3>Financial responsibility</h3>
                <p>I understand that I am financially responsible for tuition, fees, and other charges not covered by financial aid, including changes resulting from enrollment or eligibility adjustments.</p>
                <h3>Electronic consent</h3>
                <p>I consent to conduct university business electronically and understand that completing this authenticated process represents my electronic signature.</p>
                """;
            await _agreements.InsertAsync(new AgreementTemplate(
                _guidGenerator.Create(), context.TenantId, "Student Bill Agreement", 1,
                new DateTime(2026, 1, 1), html,
                JsonSerializer.Serialize(new[] { "StudentName", "StudentId", "TermName", "AcceptedAt" }), true));
        }

        if (await _policies.FindAsync(x => x.TenantId == context.TenantId && x.Name == "Standard Payment Plan" && x.Version == 1) is null)
        {
            await _policies.InsertAsync(new PaymentPlanPolicy(
                _guidGenerator.Create(), context.TenantId, "Standard Payment Plan", 1, new DateTime(2026, 1, 1),
                100m, 3m, 3500m, 1500m,
                JsonSerializer.Serialize(new[] { "Residential Undergraduate", "LEAD", "American Indian College", "Oaks School of Leadership", "Oaks Church" }),
                JsonSerializer.Serialize(new[] { "September 30", "October 30", "November 30", "December 30" }),
                JsonSerializer.Serialize(new[] { "February 28", "March 30", "April 30", "May 30" }),
                JsonSerializer.Serialize(new[] { "July 15", "August 15" }), true));
        }
    }
}
