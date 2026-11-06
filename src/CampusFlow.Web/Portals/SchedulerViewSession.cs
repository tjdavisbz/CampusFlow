using System;
using System.Linq;
using System.Net.Mail;
using CampusFlow.AdvisorPortal;
using CampusFlow.StudentInformationSystems;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

public sealed class SchedulerViewSession : ITransientDependency
{
    public const string CookieName = "CampusFlow.SchedulerView";
    private readonly IDataProtector _protector;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public SchedulerViewSession(IDataProtectionProvider protectionProvider,
        ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        _protector = protectionProvider.CreateProtector("CampusFlow.SchedulerView.v1");
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public void Start(HttpContext context, AdvisorLookupResult scheduler)
    {
        if (!_currentUser.Id.HasValue || !_currentTenant.Id.HasValue) throw new InvalidOperationException("A signed-in tenant user is required.");
        if (!MailAddress.TryCreate(scheduler.Email, out _) || string.IsNullOrWhiteSpace(scheduler.DisplayName)) throw new ArgumentException("A valid scheduler is required.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var ticket = new SchedulerViewTicket(_currentUser.Id.Value, _currentTenant.Id,
            expires, scheduler.Email.Trim(), scheduler.DisplayName);
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

    internal bool TryRead(HttpContext context, out SchedulerViewTicket ticket)
    {
        ticket = null!;
        if (!_currentUser.Id.HasValue || !context.Request.Cookies.TryGetValue(CookieName, out var value))
            return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<SchedulerViewTicket>(_protector.Unprotect(value));
            if (candidate is null || candidate.ActorUserId != _currentUser.Id.Value ||
                !candidate.TenantId.HasValue || candidate.TenantId != _currentTenant.Id || candidate.ExpiresAt <= DateTimeOffset.UtcNow ||
                !MailAddress.TryCreate(candidate.Email, out _))
                return false;
            ticket = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static ClaimsIdentity CreateIdentity(SchedulerViewTicket ticket) => new(
        [new Claim(SchedulerViewClaimTypes.Email, ticket.Email),
            new Claim(SchedulerViewClaimTypes.Name, ticket.Name)], SchedulerViewClaimTypes.AuthenticationType);


}

internal sealed record SchedulerViewTicket(Guid ActorUserId, Guid? TenantId,
    DateTimeOffset ExpiresAt, string Email, string Name);
