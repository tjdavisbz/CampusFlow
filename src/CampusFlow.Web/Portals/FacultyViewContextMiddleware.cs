using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;

namespace CampusFlow.Web.Portals;

public sealed class FacultyViewContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FacultyViewSession session,
        IPermissionChecker permissionChecker, ILogger<FacultyViewContextMiddleware> logger)
    {
        if (!session.TryRead(context, out var ticket))
        {
            if (context.Request.Cookies.ContainsKey(FacultyViewSession.CookieName)) session.End(context);
            await next(context);
            return;
        }

        if (!await permissionChecker.IsGrantedAsync(CampusFlowPermissions.FacultyImpersonation.Default))
        {
            session.End(context);
            await next(context);
            return;
        }

        context.User.AddIdentity(FacultyViewSession.CreateIdentity(ticket));
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method) &&
            !context.Request.Path.StartsWithSegments("/Admin/ImpersonateFaculty/End"))
        {
            logger.LogWarning(
                "Blocked a write request while administrator {ActorUserId} was viewing faculty {FacultyId}. TraceId={TraceId}",
                ticket.ActorUserId, ticket.FacultyId, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Faculty impersonation is read-only. Exit faculty view before making changes.");
            return;
        }

        await next(context);
    }
}
