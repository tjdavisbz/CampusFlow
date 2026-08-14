using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using CampusFlow.Students;
using CampusFlow.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.PermissionManagement;
using Volo.Abp;

namespace CampusFlow.Web.Pages.Account;

public class RegisterModel : Volo.Abp.Account.Web.Pages.Account.RegisterModel
{
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IStudentInformationSystemAdvisorLookup _advisorLookup;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;

    public RegisterModel(
        IAccountAppService accountAppService,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IGuidGenerator guidGenerator,
        IStudentInformationSystemAdvisorLookup advisorLookup,
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager)
        : base(
            accountAppService,
            schemeProvider,
            accountOptions,
            identityDynamicClaimsPrincipalContributorCache)
    {
        _studentLookups = studentLookups.ToArray();
        _studentProfileRepository = studentProfileRepository;
        _guidGenerator = guidGenerator;
        _advisorLookup = advisorLookup;
        _roleManager = roleManager;
        _permissionManager = permissionManager;
    }

    public override async Task<IActionResult> OnGetAsync()
    {
        if (!IsExternalLogin || !string.Equals(
                ExternalLoginAuthSchema,
                "MicrosoftEntra",
                StringComparison.Ordinal))
        {
            return Page();
        }

        var externalLogin = await SignInManager.GetExternalLoginInfoAsync();
        var email = externalLogin?.Principal.FindFirstValue(AbpClaimTypes.Email) ??
                    externalLogin?.Principal.FindFirstValue(ClaimTypes.Email);

        if (externalLogin is null || string.IsNullOrWhiteSpace(email))
        {
            Logger.LogWarning("Microsoft external login did not include a usable email claim.");
            return Page();
        }

        var lookup = _studentLookups.SingleOrDefault(x =>
            x.Provider == StudentInformationSystemProvider.ThesisElements);
        if (lookup is null)
        {
            Logger.LogError("The Thesis Elements student lookup provider is not registered.");
            return Page();
        }

        StudentLookupResult result;
        try
        {
            result = await lookup.FindByEmailAsync(email, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Student lookup failed during Microsoft account linking.");
            return Page();
        }

        if (result.Status == StudentLookupStatus.Matched && result.Student is not null)
        {
            await RegisterExternalUserAsync(externalLogin, result.Student.Email, result.Student.Email);

            var studentUser = await UserManager.FindByEmailAsync(result.Student.Email);
            if (studentUser is not null)
            {
                await _studentProfileRepository.InsertAsync(new StudentProfile(
                    _guidGenerator.Create(), CurrentTenant.Id, studentUser.Id, result.Student));
            }

            return await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash);
        }

        AdvisorLookupResult? advisor;
        try
        {
            advisor = await _advisorLookup.FindAsync(email, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Advisor lookup failed during Microsoft account linking.");
            return Page();
        }

        if (advisor is null)
        {
            Logger.LogWarning(
                "Microsoft account matched neither an eligible student nor an active Elements user. Student status: {Status}",
                result.Status);
            return Page();
        }

        await RegisterExternalUserAsync(externalLogin, advisor.UserName, email);
        var user = await UserManager.FindByEmailAsync(email);
        if (user is null) return Page();
        user.Name = advisor.FirstName;
        user.Surname = advisor.LastName;
        EnsureIdentitySucceeded(await UserManager.UpdateAsync(user));

        const string advisorRoleName = "advisor";
        await EnsureRoleAsync(user, advisorRoleName);
        await _permissionManager.SetForRoleAsync(
            advisorRoleName, CampusFlowPermissions.AdvisorPortal.Default, true);
        if (advisor.CanViewAll)
        {
            const string globalReviewerRoleName = "advisor-global-reviewer";
            await EnsureRoleAsync(user, globalReviewerRoleName);
            await _permissionManager.SetForRoleAsync(
                globalReviewerRoleName, CampusFlowPermissions.AdvisorPortal.Default, true);
            await _permissionManager.SetForRoleAsync(
                globalReviewerRoleName, CampusFlowPermissions.AdvisorPortal.ViewAll, true);
        }

        return await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash);
    }

    private async Task EnsureRoleAsync(IdentityUser user, string roleName)
    {
        var role = await _roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new IdentityRole(_guidGenerator.Create(), roleName, CurrentTenant.Id);
            EnsureIdentitySucceeded(await _roleManager.CreateAsync(role));
        }

        if (!await UserManager.IsInRoleAsync(user, roleName))
        {
            EnsureIdentitySucceeded(await UserManager.AddToRoleAsync(user, roleName));
        }
    }

    private static void EnsureIdentitySucceeded(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (result.Succeeded) return;
        throw new UserFriendlyException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }

    public override Task<IActionResult> OnPostAsync() => Task.FromResult<IActionResult>(Page());
}
