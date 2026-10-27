using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
namespace CampusFlow.Bibliu;

public sealed record BibliuExportResult(Guid ConfigurationId, string ConfigurationVersion,
    DateTime GeneratedAtUtc, string BillingTerm, string BibliuTerm, IReadOnlyList<string> ExpandedTerms,
    IReadOnlyDictionary<string, IReadOnlyList<Dictionary<string,string?>>> Exports);

public sealed class BibliuExportService(IRepository<BibliuConfiguration, Guid> configurations,
    IRepository<BillApproval, Guid> approvals, IEnumerable<IStudentInformationSystemTermLookup> termLookups,
    IBibliuExportReader reader, ICurrentTenant tenant) : ITransientDependency
{
    public async Task<BibliuExportResult> ExportAsync(string kind, CancellationToken cancellationToken)
    {
        if (kind is not ("all" or "users" or "courses" or "schedule" or "enrollments"))
            throw new UserFriendlyException("Choose users, courses, schedule, enrollments or all.");
        if (!tenant.Id.HasValue) throw new UserFriendlyException("Select a CampusFlow tenant before requesting BibliU exports.");
        var config = await configurations.FindAsync(x => x.TenantId == tenant.Id, cancellationToken: cancellationToken);
        if (config is null || !config.IsEnabled) throw new UserFriendlyException("BibliU exports are not enabled for this tenant.");
        var terms = await termLookups.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements).GetTermsAsync(cancellationToken);
        var billing = terms.SingleOrDefault(x => x.ExternalTermId == config.BillingTermId);
        var bibliu = terms.SingleOrDefault(x => x.ExternalTermId == config.BibliuTermId);
        if (billing is null || bibliu is null) throw new UserFriendlyException("A configured BibliU term is unavailable in Elements. Review the configuration.");
        var expanded = BibliuRules.ExpandedTerms(bibliu.DisplayName).Where(n => terms.Any(t => t.DisplayName == n)).ToArray();
        var approvedIds = kind is "users" or "all"
            ? (await approvals.GetListAsync(x => x.TenantId == tenant.Id && x.ExternalTermId == config.BillingTermId &&
                x.AcceptedAt != null && x.Status == BillApprovalStatus.Approved, cancellationToken: cancellationToken))
                .Select(x => x.ExternalStudentId).Distinct().ToArray() : [];
        var request = new BibliuExportRequest(int.Parse(config.BillingTermId, CultureInfo.InvariantCulture),
            int.Parse(config.BibliuTermId, CultureInfo.InvariantCulture), config.StoreNumber, expanded, approvedIds);
        var exports = new Dictionary<string,IReadOnlyList<Dictionary<string,string?>>>();
        foreach (var name in kind == "all" ? new[]{"users","courses","schedule","enrollments"} : new[]{kind})
            exports[name] = await reader.ReadAsync(name, request, cancellationToken);
        return new(config.Id, config.ConcurrencyStamp, DateTime.UtcNow, billing.DisplayName, bibliu.DisplayName, expanded, exports);
    }
}
