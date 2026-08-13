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
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace CampusFlow.Web.Pages.Account;

public class RegisterModel : Volo.Abp.Account.Web.Pages.Account.RegisterModel
{
    private readonly IReadOnlyCollection<IStudentInformationSystemStudentLookup> _studentLookups;
    private readonly IRepository<StudentProfile, Guid> _studentProfileRepository;
    private readonly IGuidGenerator _guidGenerator;

    public RegisterModel(
        IAccountAppService accountAppService,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        IEnumerable<IStudentInformationSystemStudentLookup> studentLookups,
        IRepository<StudentProfile, Guid> studentProfileRepository,
        IGuidGenerator guidGenerator)
        : base(
            accountAppService,
            schemeProvider,
            accountOptions,
            identityDynamicClaimsPrincipalContributorCache)
    {
        _studentLookups = studentLookups.ToArray();
        _studentProfileRepository = studentProfileRepository;
        _guidGenerator = guidGenerator;
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

        if (result.Status != StudentLookupStatus.Matched || result.Student is null)
        {
            Logger.LogWarning(
                "Microsoft account could not be linked to exactly one active local student record. Status: {Status}",
                result.Status);
            return Page();
        }

        await RegisterExternalUserAsync(
            externalLogin,
            result.Student.Email,
            result.Student.Email);

        var user = await UserManager.FindByEmailAsync(result.Student.Email);
        if (user is not null)
        {
            await _studentProfileRepository.InsertAsync(new StudentProfile(
                _guidGenerator.Create(),
                CurrentTenant.Id,
                user.Id,
                result.Student));
        }

        return await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash);
    }

    public override Task<IActionResult> OnPostAsync() => Task.FromResult<IActionResult>(Page());
}
