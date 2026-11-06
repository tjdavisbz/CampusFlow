using System.Linq;
using CampusFlow.AdvisorPortal;
using Microsoft.AspNetCore.Http;
namespace CampusFlow.Web.Portals;
public sealed class CurrentSchedulerView(IHttpContextAccessor accessor) : ICurrentSchedulerView
{
    public string? Email => accessor.HttpContext?.User.Identities
        .Where(x => x.AuthenticationType == SchedulerViewClaimTypes.AuthenticationType)
        .SelectMany(x => x.Claims).FirstOrDefault(x => x.Type == SchedulerViewClaimTypes.Email)?.Value;
    public bool IsActive => !string.IsNullOrWhiteSpace(Email);
}
