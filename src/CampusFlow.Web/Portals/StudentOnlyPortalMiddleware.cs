using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CampusFlow.Web.Portals;

public sealed class StudentOnlyPortalMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, StudentOnlyAccountAccess accountAccess)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            IsPrivilegedPath(context.Request.Path) &&
            await accountAccess.IsStudentOnlyAsync())
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("This account has access to the student portal only.");
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
}
