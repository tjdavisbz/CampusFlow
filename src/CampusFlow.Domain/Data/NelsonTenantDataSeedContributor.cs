using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.TenantManagement;

namespace CampusFlow.Data;

public class NelsonTenantDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string TenantName = "nelson";

    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;

    public NelsonTenantDataSeedContributor(
        ITenantRepository tenantRepository,
        TenantManager tenantManager)
    {
        _tenantRepository = tenantRepository;
        _tenantManager = tenantManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var existingTenants = context.TenantId.HasValue ? [] : await _tenantRepository.GetListAsync();
        if (context.TenantId.HasValue || existingTenants.Any(x =>
                string.Equals(x.Name, TenantName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var tenant = await _tenantManager.CreateAsync(TenantName);
        await _tenantRepository.InsertAsync(tenant, autoSave: true);
    }
}
