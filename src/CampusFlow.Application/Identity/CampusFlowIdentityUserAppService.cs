using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MicrosoftIdentityOptions = Microsoft.AspNetCore.Identity.IdentityOptions;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace CampusFlow.Identity;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityUserAppService), typeof(IdentityUserAppService))]
public class CampusFlowIdentityUserAppService : IdentityUserAppService
{
    private readonly ICamsStaffEmailLookup _staffEmails;
    private readonly IRepository<IdentityUser, Guid> _users;

    public CampusFlowIdentityUserAppService(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        IOptions<MicrosoftIdentityOptions> identityOptions,
        IPermissionChecker permissionChecker,
        ICamsStaffEmailLookup staffEmails,
        IRepository<IdentityUser, Guid> users)
        : base(userManager, userRepository, roleRepository, identityOptions, permissionChecker)
    {
        _staffEmails = staffEmails;
        _users = users;
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<PagedResultDto<IdentityUserDto>> GetListAsync(GetIdentityUsersInput input)
    {
        if (!string.Equals(CurrentTenant.Name, "nelson", StringComparison.OrdinalIgnoreCase))
            return await base.GetListAsync(input);

        var allowedEmails = (await _staffEmails.GetNormalizedEmailsAsync()).ToArray();
        var query = StaffUserDirectoryQuery.ApplyFilter(await _users.GetQueryableAsync(),
            allowedEmails, input.Filter);
        var count = await AsyncExecuter.CountAsync(query);
        var users = await AsyncExecuter.ToListAsync(
            StaffUserDirectoryQuery.ApplySort(query, input.Sorting)
                .Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<IdentityUserDto>(count,
            ObjectMapper.Map<List<IdentityUser>, List<IdentityUserDto>>(users));
    }
}

public static class StaffUserDirectoryQuery
{
    public static IQueryable<IdentityUser> ApplyFilter(IQueryable<IdentityUser> users,
        IReadOnlyCollection<string> normalizedStaffEmails, string? filter)
    {
        var emails = normalizedStaffEmails.ToArray();
        users = users.Where(user => emails.Contains(user.NormalizedEmail));
        if (string.IsNullOrWhiteSpace(filter)) return users;

        var search = filter.Trim().ToUpperInvariant();
        return users.Where(user =>
            user.NormalizedUserName.Contains(search) || user.NormalizedEmail.Contains(search) ||
            (user.Name != null && user.Name.ToUpper().Contains(search)) ||
            (user.Surname != null && user.Surname.ToUpper().Contains(search)));
    }

    public static IOrderedQueryable<IdentityUser> ApplySort(IQueryable<IdentityUser> users, string? sorting)
    {
        // Only allow the columns supported by the Users screen; don't execute arbitrary client sort expressions.
        var parts = (sorting ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var column = parts.Length == 0 ? "username" : parts[0].ToLowerInvariant();
        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return (column, descending) switch
        {
            ("email", false) => users.OrderBy(user => user.Email).ThenBy(user => user.Id),
            ("email", true) => users.OrderByDescending(user => user.Email).ThenBy(user => user.Id),
            ("name", false) => users.OrderBy(user => user.Name).ThenBy(user => user.Id),
            ("name", true) => users.OrderByDescending(user => user.Name).ThenBy(user => user.Id),
            ("surname", false) => users.OrderBy(user => user.Surname).ThenBy(user => user.Id),
            ("surname", true) => users.OrderByDescending(user => user.Surname).ThenBy(user => user.Id),
            ("creationtime", false) => users.OrderBy(user => user.CreationTime).ThenBy(user => user.Id),
            ("creationtime", true) => users.OrderByDescending(user => user.CreationTime).ThenBy(user => user.Id),
            ("username", true) => users.OrderByDescending(user => user.UserName).ThenBy(user => user.Id),
            _ => users.OrderBy(user => user.UserName).ThenBy(user => user.Id)
        };
    }
}
