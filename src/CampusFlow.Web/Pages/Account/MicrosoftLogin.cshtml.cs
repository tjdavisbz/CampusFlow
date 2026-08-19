using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using CampusFlowIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CampusFlow.Web.Pages.Account;

[AllowAnonymous]
public class MicrosoftLoginModel : CampusFlowPageModel
{
    private const string AuthenticationScheme = "MicrosoftEntra";
    private readonly SignInManager<CampusFlowIdentityUser> _signInManager;

    public MicrosoftLoginModel(SignInManager<CampusFlowIdentityUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public IActionResult OnGet(string? returnUrl = null, string? returnUrlHash = null)
    {
        var callbackUrl = Url.Page(
            "/Account/Login",
            "ExternalLoginCallback",
            new { returnUrl = returnUrl ?? Url.Content("~/"), returnUrlHash });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            AuthenticationScheme,
            callbackUrl);

        return Challenge(properties, AuthenticationScheme);
    }
}
