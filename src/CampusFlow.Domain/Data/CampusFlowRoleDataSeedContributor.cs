using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace CampusFlow.Data;

public class CampusFlowRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<IdentityRole, Guid> _roles;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly IGuidGenerator _guidGenerator;

    public CampusFlowRoleDataSeedContributor(IRepository<IdentityRole, Guid> roles,
        IPermissionDataSeeder permissionDataSeeder, IGuidGenerator guidGenerator)
    {
        _roles = roles;
        _permissionDataSeeder = permissionDataSeeder;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!context.TenantId.HasValue) return;
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Payment Plan Manager",
            CampusFlowPermissions.Admin.Default, CampusFlowPermissions.Admin.PaymentPlans);
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Bill Approval Manager",
            CampusFlowPermissions.Admin.Default, CampusFlowPermissions.Admin.BillApproval,
            CampusFlowPermissions.Admin.PaymentPlans);
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Registration Manager",
            CampusFlowPermissions.Admin.Default, CampusFlowPermissions.Admin.RegistrationRules,
            CampusFlowPermissions.AdvisorPortal.Default, CampusFlowPermissions.AdvisorPortal.ManageRouting);
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Advisor", CampusFlowPermissions.AdvisorPortal.Default);
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Student Support",
            CampusFlowPermissions.Admin.Default, CampusFlowPermissions.StudentImpersonation.Default);
        await EnsureRoleAsync(context.TenantId.Value, "CampusFlow Access Administrator",
            CampusFlowPermissions.Admin.Default, CampusFlowPermissions.Admin.AccessManagement,
            "AbpIdentity.Users", "AbpIdentity.Users.Update",
            "AbpIdentity.Roles", "AbpIdentity.Roles.ManagePermissions");
    }

    private async Task EnsureRoleAsync(Guid tenantId, string roleName, params string[] permissions)
    {
        var role = (await _roles.GetListAsync()).FirstOrDefault(x =>
            string.Equals(x.Name, roleName, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            role = new IdentityRole(_guidGenerator.Create(), roleName, tenantId);
            await _roles.InsertAsync(role, autoSave: true);
        }
        await _permissionDataSeeder.SeedAsync(RolePermissionValueProvider.ProviderName,
            role.Name, permissions, tenantId);
    }
}
