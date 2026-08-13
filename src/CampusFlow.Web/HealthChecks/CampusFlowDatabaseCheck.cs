using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace CampusFlow.Web.HealthChecks;

public class CampusFlowDatabaseCheck : IHealthCheck, ITransientDependency
{
    protected readonly IIdentityRoleRepository IdentityRoleRepository;

    public CampusFlowDatabaseCheck(IIdentityRoleRepository identityRoleRepository)
    {
        IdentityRoleRepository = identityRoleRepository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await IdentityRoleRepository.GetListAsync(sorting: nameof(IdentityRole.Id), maxResultCount: 1, cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy($"Could connect to database and get record.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy($"Error when trying to get database record. ", e);
        }
    }
}
