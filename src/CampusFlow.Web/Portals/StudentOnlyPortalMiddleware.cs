using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CampusFlow.Web.Portals;

public sealed class StudentOnlyPortalMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StudentOnlyAccountAccess accountAccess)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            IsPrivilegedPath(context.Request.Path) &&
            ((IsStaffOnlyPath(context.Request.Path) && !await accountAccess.HasStaffAccessAsync()) ||
             await accountAccess.IsStudentOnlyAsync()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("This account does not have access to this portal area.");
            return;
        }

        await next(context);
    }

    public static bool IsPrivilegedPath(PathString path) =>
        path.StartsWithSegments("/Admin") ||
        path.StartsWithSegments("/Advisor") ||
        path.StartsWithSegments("/Faculty") ||
        path.StartsWithSegments("/Identity") ||
        path.StartsWithSegments("/TenantManagement") ||
        path.StartsWithSegments("/SettingManagement") ||
        path.StartsWithSegments("/PermissionManagement") ||
        path.StartsWithSegments("/FeatureManagement") ||
        path.StartsWithSegments("/api/identity") ||
        path.StartsWithSegments("/api/permission-management") ||
        path.StartsWithSegments("/api/tenant-management") ||
        path.StartsWithSegments("/api/setting-management") ||
        path.StartsWithSegments("/api/feature-management") ||
        (path.StartsWithSegments("/api/app") &&
         !path.StartsWithSegments("/api/app/course-selection") &&
         !path.StartsWithSegments("/api/app/meal-plan"));

    public static bool IsStaffOnlyPath(PathString path) =>
        IsPrivilegedPath(path) && !path.StartsWithSegments("/Faculty");
}
