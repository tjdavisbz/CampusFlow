using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Data;

public class BillApprovalTermConfigurationDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<BillApprovalTermConfiguration, Guid> _configurations;
    private readonly IRepository<AgreementTemplate, Guid> _agreements;
    private readonly IRepository<PaymentPlanPolicy, Guid> _paymentPlans;
    private readonly IReadOnlyCollection<IStudentInformationSystemTermLookup> _termLookups;
    private readonly IGuidGenerator _guidGenerator;

    public BillApprovalTermConfigurationDataSeedContributor(
        IRepository<BillApprovalTermConfiguration, Guid> configurations,
        IRepository<AgreementTemplate, Guid> agreements,
        IRepository<PaymentPlanPolicy, Guid> paymentPlans,
        IEnumerable<IStudentInformationSystemTermLookup> termLookups, IGuidGenerator guidGenerator)
    {
        _configurations = configurations; _agreements = agreements; _paymentPlans = paymentPlans;
        _termLookups = termLookups.ToArray(); _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue || await _configurations.AnyAsync()) return;
        var agreement = (await _agreements.GetListAsync()).Where(x => x.IsPublished).OrderByDescending(x => x.Version).FirstOrDefault();
        var paymentPlan = (await _paymentPlans.GetListAsync()).Where(x => x.IsPublished).OrderByDescending(x => x.Version).FirstOrDefault();
        var lookup = _termLookups.SingleOrDefault(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (agreement is null || paymentPlan is null || lookup is null) return;
        foreach (var term in await lookup.GetTermsAsync())
            await _configurations.InsertAsync(new BillApprovalTermConfiguration(_guidGenerator.Create(), context.TenantId,
                term.ExternalTermId, term.TermCode, term.DisplayName, term.StartDate.Date.AddDays(-30),
                term.EndDate.Date.AddDays(1).AddTicks(-1), false, agreement.Id, paymentPlan.Id));
    }
}
