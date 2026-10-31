using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using CampusFlow.Students;
using CampusFlow.Permissions;
using CampusFlow.Web.Portals;
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
    private readonly FacultyPortalReader _facultyPortalReader;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IConfiguration _configuration;

    public RegisterModel(
        IAccountAppService accountAppService,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IGuidGenerator guidGenerator,
        IStudentInformationSystemAdvisorLookup advisorLookup,
        FacultyPortalReader facultyPortalReader,
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager,
        IConfiguration configuration)
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
        _facultyPortalReader = facultyPortalReader;
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _configuration = configuration;
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

        StudentLookupResult? result = null;
        try
        {
            result = await lookup.FindByEmailAsync(email, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Student lookup failed during Microsoft account linking.");
        }

        if (result?.Status == StudentLookupStatus.Matched && result.Student is not null)
        {
            await RegisterExternalUserAsync(externalLogin, result.Student.Email, result.Student.Email);

            var studentUser = await UserManager.FindByEmailAsync(result.Student.Email);
            if (studentUser is not null)
            {
                var profile = await _studentProfileRepository.FindAsync(x =>
                    x.Provider == result.Student.Provider &&
                    x.ExternalStudentId == result.Student.ExternalStudentId);
                if (profile is null)
                    await _studentProfileRepository.InsertAsync(new StudentProfile(
                        _guidGenerator.Create(), CurrentTenant.Id, studentUser.Id, result.Student));
                else
                {
                    profile.AssignUser(studentUser.Id);
                    profile.Update(result.Student);
                    await _studentProfileRepository.UpdateAsync(profile);
                }
            }

            return await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash);
        }

        AdvisorLookupResult? advisor = null;
        try
        {
            advisor = await _advisorLookup.FindAsync(email, HttpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Advisor lookup failed during Microsoft account linking.");
        }

        if (advisor is null)
        {
            // Faculty portal membership is independent of CAMSUser. In particular,
            // a disabled or absent staff login must not prevent a faculty member
            // with a matching active faculty address from signing in.
            FacultyIdentity? faculty;
            try
            {
                faculty = await _facultyPortalReader.FindFacultyAsync(email, HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Faculty lookup failed during Microsoft account linking.");
                return Page();
            }

            if (faculty is null)
            {
                Logger.LogWarning(
                    "Microsoft account matched neither an eligible student, active Elements user, nor faculty portal record. Student status: {Status}",
                    result?.Status.ToString() ?? "Unavailable");
                return Page();
            }

            await RegisterExternalUserAsync(externalLogin, email, email);
            return !string.IsNullOrWhiteSpace(ReturnUrl) &&
                   ReturnUrl.StartsWith("/Faculty", StringComparison.OrdinalIgnoreCase)
                ? await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash)
                : RedirectToPage("/Faculty/Index");
        }

        await RegisterExternalUserAsync(externalLogin, advisor.UserName, email);
        var user = await UserManager.FindByEmailAsync(email);
        if (user is null) return Page();
        user.Name = advisor.FirstName;
        user.Surname = advisor.LastName;
        EnsureIdentitySucceeded(await UserManager.UpdateAsync(user));
        await EnsureConfiguredFullAdministratorAsync(user, email);

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

    private async Task EnsureConfiguredFullAdministratorAsync(IdentityUser user, string email)
    {
        var administratorEmails = _configuration
            .GetSection("Administration:FullAdministratorEmails")
            .Get<string[]>() ?? [];
        if (!administratorEmails.Contains(email, StringComparer.OrdinalIgnoreCase)) return;

        const string administratorRoleName = "admin";
        var administratorRole = await _roleManager.FindByNameAsync(administratorRoleName);
        if (administratorRole is null)
        {
            throw new UserFriendlyException("The tenant administrator role has not been initialized.");
        }

        if (!await UserManager.IsInRoleAsync(user, administratorRoleName))
        {
            EnsureIdentitySucceeded(await UserManager.AddToRoleAsync(user, administratorRoleName));
        }
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
