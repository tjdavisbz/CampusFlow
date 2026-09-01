using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CampusFlow.Web.Pages.Account;

[Authorize]
public class LogoutModel : CampusFlowPageModel
{
    private const string MicrosoftAuthenticationScheme = "MicrosoftEntra";

    public IActionResult OnGet()
    {
        return SignOutFromCampusFlowAndMicrosoft();
    }

    public IActionResult OnPost()
    {
        return SignOutFromCampusFlowAndMicrosoft();
    }

    private SignOutResult SignOutFromCampusFlowAndMicrosoft()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Page("/Account/SignedOut")
        };

        return SignOut(
            properties,
            IdentityConstants.ApplicationScheme,
            MicrosoftAuthenticationScheme);
    }
}
