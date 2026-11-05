using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.Http;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace CampusFlow.Web.Students;

public interface IStudentTermSelection
{
    Task<StudentInformationSystemTerm?> ResolveAsync(StudentProfile profile, IStudentInformationSystemTermLookup lookup,
        string? requestedTermId = null, string? defaultTermId = null, CancellationToken cancellationToken = default);
}

// The preference is specific to the signed-in user, tenant, and viewed student.
// Term identifiers are always revalidated against the provider; the cookie grants no access.
public class StudentTermSelection(IHttpContextAccessor accessor, ICurrentTenant tenant, ICurrentUser user)
    : IStudentTermSelection
{
    public async Task<StudentInformationSystemTerm?> ResolveAsync(StudentProfile profile, IStudentInformationSystemTermLookup lookup,
        string? requestedTermId = null, string? defaultTermId = null, CancellationToken cancellationToken = default)
    {
        var context = accessor.HttpContext;
        var key = "CampusFlow.Term." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{tenant.Id}:{user.Id}:{profile.Id}:{profile.Provider}")))[..24];
        if (requestedTermId is null && defaultTermId is null && context?.Items[key] is StudentInformationSystemTerm resolved)
            return resolved;
        var terms = await lookup.GetTermsAsync(cancellationToken);
        var stored = context?.Request.Cookies[key];
        var selected = terms.FirstOrDefault(x => x.ExternalTermId == requestedTermId)
            ?? terms.FirstOrDefault(x => x.ExternalTermId == stored)
            ?? terms.FirstOrDefault(x => x.ExternalTermId == defaultTermId)
            ?? await lookup.GetCurrentTermAsync(cancellationToken);
        if (selected is not null && context is not null) context.Items[key] = selected;
        if (selected is not null && context is not null && !context.Response.HasStarted && user.Id.HasValue)
            context.Response.Cookies.Append(key, selected.ExternalTermId, new CookieOptions
            { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true, Path = "/" });
        return selected;
    }
}
