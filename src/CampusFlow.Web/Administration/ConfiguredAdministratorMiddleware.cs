using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;

namespace CampusFlow.Web.Administration;

public sealed class ConfiguredAdministratorMiddleware
{
    private const string AdministratorRoleName = "admin";
    private readonly RequestDelegate _next;

    public ConfiguredAdministratorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IdentityUserManager userManager,
        IdentityRoleManager roleManager,
        IConfiguration configuration,
        ILogger<ConfiguredAdministratorMiddleware> logger)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            !context.User.IsInRole(AdministratorRoleName))
        {
            var email = context.User.FindFirstValue(AbpClaimTypes.Email) ??
                        context.User.FindFirstValue(ClaimTypes.Email);
            var administratorEmails = configuration
                .GetSection("Administration:FullAdministratorEmails")
                .Get<string[]>() ?? [];

            if (!string.IsNullOrWhiteSpace(email) &&
                administratorEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                var user = await userManager.FindByEmailAsync(email);
                var role = await roleManager.FindByNameAsync(AdministratorRoleName);

                if (user is not null && role is not null &&
                    !await userManager.IsInRoleAsync(user, AdministratorRoleName))
                {
                    var result = await userManager.AddToRoleAsync(user, AdministratorRoleName);
                    if (!result.Succeeded)
                    {
                        logger.LogError(
                            "Could not grant the configured full administrator role to user {UserId}: {Errors}",
                            user.Id,
                            string.Join(" ", result.Errors.Select(error => error.Description)));
                    }
                    else
                    {
                        logger.LogInformation(
                            "Granted the configured full administrator role to user {UserId}.",
                            user.Id);
                    }
                }
            }
        }

        await _next(context);
    }
}
