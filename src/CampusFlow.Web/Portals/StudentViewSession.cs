using System;
using System.Security.Claims;
using System.Text.Json;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Students;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace CampusFlow.Web.Portals;

public sealed class StudentViewSession : ITransientDependency
{
    public const string CookieName = "CampusFlow.StudentView";
    private readonly IDataProtector _protector;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public StudentViewSession(
        IDataProtectionProvider protectionProvider, ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        _protector = protectionProvider.CreateProtector("CampusFlow.StudentView.v1");
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public void Start(HttpContext context, StudentInformationSystemStudent student)
    {
        if (!_currentUser.Id.HasValue) throw new InvalidOperationException("A signed-in user is required.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var ticket = new StudentViewTicket(
            _currentUser.Id.Value, _currentTenant.Id, expires, student.Provider,
            student.ExternalStudentId, student.StudentId, student.Email, student.FirstName,
            student.PreferredName, student.LastName);
        var value = _protector.Protect(JsonSerializer.Serialize(ticket));
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Expires = expires
        });
    }

    public void End(HttpContext context) => context.Response.Cookies.Delete(CookieName);

    internal bool TryRead(HttpContext context, out StudentViewTicket ticket)
    {
        ticket = null!;
        if (!_currentUser.Id.HasValue || !context.Request.Cookies.TryGetValue(CookieName, out var value))
            return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<StudentViewTicket>(_protector.Unprotect(value));
            if (candidate is null || candidate.ActorUserId != _currentUser.Id.Value ||
                candidate.TenantId != _currentTenant.Id || candidate.ExpiresAt <= DateTimeOffset.UtcNow)
                return false;
            ticket = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static ClaimsIdentity CreateIdentity(StudentViewTicket ticket)
    {
        var claims = new[]
        {
            new Claim(StudentViewClaimTypes.Provider, ticket.Provider.ToString()),
            new Claim(StudentViewClaimTypes.ExternalStudentId, ticket.ExternalStudentId),
            new Claim(StudentViewClaimTypes.StudentId, ticket.StudentId),
            new Claim(StudentViewClaimTypes.Email, ticket.Email),
            new Claim(StudentViewClaimTypes.FirstName, ticket.FirstName),
            new Claim(StudentViewClaimTypes.PreferredName, ticket.PreferredName ?? string.Empty),
            new Claim(StudentViewClaimTypes.LastName, ticket.LastName)
        };
        return new ClaimsIdentity(claims, "StudentView");
    }
}

internal sealed record StudentViewTicket(
    Guid ActorUserId, Guid? TenantId, DateTimeOffset ExpiresAt,
    StudentInformationSystemProvider Provider, string ExternalStudentId, string StudentId,
    string Email, string FirstName, string? PreferredName, string LastName);
