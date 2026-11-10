using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Auditing;

namespace CampusFlow.FinancialAid;

[Authorize(CampusFlowPermissions.Admin.ReconcileAwardCodes)]
public class ReconcileAwardCodesAppService : CampusFlowAppService, IReconcileAwardCodesAppService
{
    public const int BatchSize = 100;
    private readonly IStudentInformationSystemAwardCodeMaintenance _maintenance;
    private readonly ILogger<ReconcileAwardCodesAppService> _logger;
    private readonly Volo.Abp.MultiTenancy.ICurrentTenant _tenant;

    public ReconcileAwardCodesAppService(IEnumerable<IStudentInformationSystemAwardCodeMaintenance> maintenance,
        ILogger<ReconcileAwardCodesAppService> logger, Volo.Abp.MultiTenancy.ICurrentTenant tenant)
    {
        _maintenance = maintenance.Single(x => x.Provider == StudentInformationSystemProvider.ThesisElements);
        _logger = logger;
        _tenant = tenant;
    }

    public Task<IReadOnlyList<string>> GetTermsAsync() { RequireNelson(); return _maintenance.GetTermsAsync(); }
    public Task<bool> CanApplyAsync() { RequireNelson(); return Task.FromResult(_maintenance.CanWrite); }

    public async Task<IReadOnlyList<AwardCodeMismatch>> PreviewAsync(string term)
    {
        RequireNelson();
        if (string.IsNullOrWhiteSpace(term)) return [];
        return await _maintenance.GetMismatchesAsync(term.Trim(), BatchSize + 1);
    }

    [Audited]
    public async Task<ReconcileAwardCodesResult> ApplyAsync(string term, string previewAwardIds)
    {
        RequireNelson();
        if (!_maintenance.CanWrite)
            throw new UserFriendlyException("Award-code updates are not enabled for this portal and Elements API environment.");
        if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(previewAwardIds))
            throw new UserFriendlyException("Preview the awards for a term before applying changes.");
        var preview = await PreviewAsync(term);
        if (preview.Count == 0) throw new UserFriendlyException("No mismatched awards remain for this term.");
        var ids = string.Join(',', preview.Take(BatchSize).Select(x => x.AwardId));
        if (!string.Equals(ids, previewAwardIds, StringComparison.Ordinal))
            throw new UserFriendlyException("The award list changed. Preview it again before applying changes.");

        var updated = 0;
        foreach (var award in preview.Take(BatchSize))
        {
            try
            {
                await _maintenance.UpdateAsync(award);
                updated++;
                _logger.LogWarning("Award code reconciliation updated award {AwardId} for term {Term} by {ActorId}.",
                    award.AwardId, term, CurrentUser.Id);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Award code reconciliation stopped at award {AwardId} for term {Term}; {Updated} completed.",
                    award.AwardId, term, updated);
                return new ReconcileAwardCodesResult(updated,
                    $"Stopped after {updated} update(s). Award {award.AwardId} failed; the remaining awards were not changed.");
            }
        }
        return new ReconcileAwardCodesResult(updated, null);
    }
    private void RequireNelson()
    {
        if (!_tenant.Id.HasValue || !string.Equals(_tenant.Name,"nelson",StringComparison.OrdinalIgnoreCase))
            throw new UserFriendlyException("Select Nelson University before previewing award codes.");
    }
}
