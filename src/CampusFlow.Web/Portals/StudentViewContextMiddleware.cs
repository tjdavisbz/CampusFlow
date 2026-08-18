using System.Threading.Tasks;
using CampusFlow.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;

namespace CampusFlow.Web.Portals;

public sealed class StudentViewContextMiddleware
{
    private readonly RequestDelegate _next;

    public StudentViewContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context, StudentViewSession session, IPermissionChecker permissionChecker,
        ILogger<StudentViewContextMiddleware> logger)
    {
        if (!session.TryRead(context, out var ticket))
        {
            if (context.Request.Cookies.ContainsKey(StudentViewSession.CookieName)) session.End(context);
            await _next(context);
            return;
        }

        if (!await permissionChecker.IsGrantedAsync(CampusFlowPermissions.StudentImpersonation.Default))
        {
            session.End(context);
            await _next(context);
            return;
        }

        context.User.AddIdentity(StudentViewSession.CreateIdentity(ticket));
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
            !HttpMethods.IsOptions(context.Request.Method) &&
            !context.Request.Path.StartsWithSegments("/Admin/ImpersonateStudent/End"))
        {
            logger.LogWarning(
                "Blocked a write request while administrator {ActorUserId} was viewing student {ExternalStudentId}. TraceId={TraceId}",
                ticket.ActorUserId, ticket.ExternalStudentId, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Student impersonation is read-only. Exit student view before making changes.");
            return;
        }

        await _next(context);
    }
}
