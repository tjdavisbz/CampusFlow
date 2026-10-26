using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

public static class FacultyViewClaimTypes
{
    public const string FacultyId = "CampusFlow.FacultyView.FacultyId";
    public const string FacultyName = "CampusFlow.FacultyView.FacultyName";
}

public sealed class FacultyViewSession : ITransientDependency
{
    public const string CookieName = "CampusFlow.FacultyView";
    private readonly IDataProtector _protector;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public FacultyViewSession(IDataProtectionProvider protectionProvider,
        ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        _protector = protectionProvider.CreateProtector("CampusFlow.FacultyView.v1");
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public void Start(HttpContext context, FacultyIdentity faculty)
    {
        if (!_currentUser.Id.HasValue) throw new InvalidOperationException("A signed-in user is required.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var ticket = new FacultyViewTicket(_currentUser.Id.Value, _currentTenant.Id,
            expires, faculty.FacultyId, faculty.DisplayName);
        context.Response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(ticket)),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Expires = expires
            });
    }

    public void End(HttpContext context) => context.Response.Cookies.Delete(CookieName);

    internal bool TryRead(HttpContext context, out FacultyViewTicket ticket)
    {
        ticket = null!;
        if (!_currentUser.Id.HasValue || !context.Request.Cookies.TryGetValue(CookieName, out var value))
            return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<FacultyViewTicket>(_protector.Unprotect(value));
            if (candidate is null || candidate.ActorUserId != _currentUser.Id.Value ||
                candidate.TenantId != _currentTenant.Id || candidate.ExpiresAt <= DateTimeOffset.UtcNow ||
                candidate.FacultyId <= 0)
                return false;
            ticket = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static ClaimsIdentity CreateIdentity(FacultyViewTicket ticket) => new(
        [new Claim(FacultyViewClaimTypes.FacultyId, ticket.FacultyId.ToString()),
            new Claim(FacultyViewClaimTypes.FacultyName, ticket.FacultyName)], "FacultyView");

    public static int? GetFacultyId(HttpContext context) =>
        int.TryParse(context.User.Claims.FirstOrDefault(x => x.Type == FacultyViewClaimTypes.FacultyId)?.Value,
            out var id) && id > 0 ? id : null;
}

internal sealed record FacultyViewTicket(Guid ActorUserId, Guid? TenantId,
    DateTimeOffset ExpiresAt, int FacultyId, string FacultyName);
