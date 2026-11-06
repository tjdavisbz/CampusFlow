using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.CourseSelections;
using CampusFlow.Permissions;
using CampusFlow.Students;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
namespace CampusFlow.Web.Portals;

public sealed class SchedulerViewContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SchedulerViewSession session,
        IPermissionChecker permissions, IRepository<AdvisorAssignment, Guid> assignments,
        ILogger<SchedulerViewContextMiddleware> logger)
    {
        if (!session.TryRead(context, out var ticket))
        {
            if (context.Request.Cookies.ContainsKey(SchedulerViewSession.CookieName))
            {
                session.End(context); await ContinueAfterEndAsync(context); return;
            }
            await next(context); return;
        }
        if (!await permissions.IsGrantedAsync(CampusFlowPermissions.SchedulerImpersonation.Default) ||
            context.User.HasClaim(x => x.Type == StudentViewClaimTypes.ExternalStudentId || x.Type == FacultyViewClaimTypes.FacultyId))
        {
            session.End(context); await ContinueAfterEndAsync(context); return;
        }
        var now = DateTime.UtcNow;
        var active = await assignments.GetListAsync(x => x.IsActive && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo > now));
        if (!active.Any(x => string.Equals(x.AdvisorEmail, ticket.Email, StringComparison.OrdinalIgnoreCase)))
        {
            session.End(context); await ContinueAfterEndAsync(context); return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.User.AddIdentity(SchedulerViewSession.CreateIdentity(ticket));
        var exit = HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/Admin/ImpersonateScheduler/End", StringComparison.OrdinalIgnoreCase);
        // Mail authorization/retry GET handlers can have side effects, so only the ordinary scheduler GET pages are allowed.
        var read = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)) &&
            !(context.Request.Path.StartsWithSegments("/Advisor") && context.Request.Query.ContainsKey("handler"));
        if (!read && !exit)
        {
            logger.LogWarning("Blocked a write while administrator {ActorUserId} viewed scheduler {SchedulerEmail}. TraceId={TraceId}", ticket.ActorUserId, ticket.Email, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Scheduler impersonation is read-only. Exit scheduler view before making changes.");
            return;
        }
        await next(context);
    }
    private async Task ContinueAfterEndAsync(HttpContext context)
    {
        var exit = HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Equals("/Admin/ImpersonateScheduler/End", StringComparison.OrdinalIgnoreCase);
        var read = (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)) &&
            !(context.Request.Path.StartsWithSegments("/Advisor") && context.Request.Query.ContainsKey("handler"));
        if (!read && !exit)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Scheduler view ended. Reload the page before making changes.");
            return;
        }
        await next(context);
    }

}
