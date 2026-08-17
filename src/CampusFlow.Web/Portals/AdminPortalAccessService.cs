using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Permissions;
using CampusFlow.StudentInformationSystems;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

public sealed class AdminPortalAccessService : ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly ICurrentUser _currentUser;
    private readonly IStudentInformationSystemAdvisorLookup _staffLookup;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IGuidGenerator _guidGenerator;

    public AdminPortalAccessService(IConfiguration configuration, ICurrentUser currentUser,
        IStudentInformationSystemAdvisorLookup staffLookup, IdentityUserManager userManager,
        IdentityRoleManager roleManager, IPermissionManager permissionManager, IGuidGenerator guidGenerator)
    {
        _configuration = configuration;
        _currentUser = currentUser;
        _staffLookup = staffLookup;
        _userManager = userManager;
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _guidGenerator = guidGenerator;
    }

    public async Task<bool> EnsureAccessAsync(string permission)
    {
        if (!_currentUser.Id.HasValue || string.IsNullOrWhiteSpace(_currentUser.Email)) return false;

        var configured = _configuration.GetSection("AdminPortal:Users").Get<AdminPortalUserAccess[]>() ?? [];
        var access = configured.FirstOrDefault(x =>
            string.Equals(x.Email, _currentUser.Email, StringComparison.OrdinalIgnoreCase));
        if (access is null || !access.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase)) return false;

        // An internal allow-list is never enough by itself: the account must also be an active CAMSUser.
        if (await _staffLookup.FindAsync(_currentUser.Email) is null) return false;

        const string roleName = "admin";
        var user = await _userManager.GetByIdAsync(_currentUser.Id.Value);
        var role = await _roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new IdentityRole(_guidGenerator.Create(), roleName, _currentUser.TenantId);
            await _roleManager.CreateAsync(role);
        }
        if (!await _userManager.IsInRoleAsync(user, roleName)) await _userManager.AddToRoleAsync(user, roleName);

        await _permissionManager.SetForRoleAsync(roleName, CampusFlowPermissions.AdminPortal.Default, true);
        await _permissionManager.SetForRoleAsync(roleName, CampusFlowPermissions.AdminPortal.PaymentPlans, true);
        return true;
    }

    private sealed class AdminPortalUserAccess
    {
        public string Email { get; set; } = string.Empty;
        public string[] Permissions { get; set; } = [];
    }
}
