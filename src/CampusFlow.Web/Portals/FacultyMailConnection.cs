using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace CampusFlow.Web.Portals;

public interface IFacultyMailTokenStore
{
    Task<string?> GetAsync(Guid userId);
    Task SetAsync(Guid userId, string protectedTicket);
}

public sealed class FacultyMailTokenStore(IdentityUserManager userManager) :
    IFacultyMailTokenStore, ITransientDependency
{
    private const string TokenName = "DelegatedMail";
    private const string TokenProvider = "CampusFlow.FacultyMail";

    public async Task<string?> GetAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await userManager.GetAuthenticationTokenAsync(user, TokenProvider, TokenName);
    }

    public async Task SetAsync(Guid userId, string protectedTicket)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("The faculty portal user no longer exists.");
        var saved = await userManager.SetAuthenticationTokenAsync(user, TokenProvider, TokenName, protectedTicket);
        if (!saved.Succeeded)
            throw new InvalidOperationException("The Microsoft mail authorization could not be saved.");
    }
}

/// <summary>Server-side delegated Graph authorization, scoped to an existing linked Microsoft login.</summary>
public sealed class FacultyMailConnection(IDataProtectionProvider protectionProvider,
    IFacultyMailTokenStore tokenStore, IConfiguration configuration,
    IHttpClientFactory httpClientFactory, ILogger<FacultyMailConnection> logger) : ITransientDependency
{
    private const string MailScope = "https://graph.microsoft.com/Mail.Send";
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("CampusFlow.FacultyMail.StoredToken.v1");

    public async Task ConnectAsync(Guid userId, string providerKey, string senderEmail,
        string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        if (expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            throw new InvalidOperationException("The Microsoft mail authorization has already expired.");
        var ticket = new MailTicket(userId, providerKey, senderEmail, accessToken, refreshToken, expiresAt);
        var protectedTicket = _protector.Protect(JsonSerializer.Serialize(ticket));
        await tokenStore.SetAsync(userId, protectedTicket);
    }

    public async Task<FacultyMailAccess?> GetAccessAsync(Guid userId, string providerKey,
        CancellationToken cancellationToken)
    {
        var protectedValue = await tokenStore.GetAsync(userId);
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        MailTicket? ticket;
        try
        {
            ticket = JsonSerializer.Deserialize<MailTicket>(_protector.Unprotect(protectedValue));
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            logger.LogWarning("Stored faculty mail authorization could not be read for user {UserId}.", userId);
            return null;
        }
        if (ticket is null || ticket.UserId != userId ||
            !string.Equals(ticket.ProviderKey, providerKey, StringComparison.Ordinal)) return null;
        if (ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return new FacultyMailAccess(ticket.SenderEmail, ticket.AccessToken);

        var clientId = configuration["MicrosoftIdentity:ClientId"];
        var clientSecret = configuration["MicrosoftIdentity:ClientSecret"];
        var tenantText = providerKey.Split(':', 2)[0];
        if (!Guid.TryParse(tenantText, out var tenantId) ||
            string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://login.microsoftonline.com/{tenantId:D}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = ticket.RefreshToken,
                ["scope"] = MailScope
            })
        };
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Faculty mail token renewal failed with status {StatusCode} for user {UserId}.",
                    (int)response.StatusCode, userId);
                return ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)
                    ? new FacultyMailAccess(ticket.SenderEmail, ticket.AccessToken) : null;
            }
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var data = document.RootElement;
            if (!data.TryGetProperty("access_token", out var accessProperty) ||
                !data.TryGetProperty("expires_in", out var durationProperty) ||
                !durationProperty.TryGetInt32(out var lifetimeSeconds) || lifetimeSeconds <= 60) return null;
            var accessToken = accessProperty.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return null;
            var replacementRefreshToken = data.TryGetProperty("refresh_token", out var refreshProperty)
                ? refreshProperty.GetString() : null;
            await ConnectAsync(userId, providerKey, ticket.SenderEmail, accessToken,
                string.IsNullOrWhiteSpace(replacementRefreshToken) ? ticket.RefreshToken : replacementRefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds));
            return new FacultyMailAccess(ticket.SenderEmail, accessToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Faculty mail token renewal could not reach Microsoft for user {UserId}.", userId);
            return ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)
                ? new FacultyMailAccess(ticket.SenderEmail, ticket.AccessToken) : null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Faculty mail token renewal timed out for user {UserId}.", userId);
            return ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)
                ? new FacultyMailAccess(ticket.SenderEmail, ticket.AccessToken) : null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Microsoft returned an invalid faculty mail token response for user {UserId}.", userId);
            return null;
        }
    }

    private sealed record MailTicket(Guid UserId, string ProviderKey, string SenderEmail,
        string AccessToken, string RefreshToken,
        DateTimeOffset ExpiresAt);
}

public sealed record FacultyMailAccess(string SenderEmail, string AccessToken);

public sealed class FacultyGraphMailSender(IHttpClientFactory httpClientFactory,
    ILogger<FacultyGraphMailSender> logger) : ITransientDependency
{
    public async Task<FacultyMailSendResult> SendAsync(string accessToken, string senderEmail,
        string subject, string body, IReadOnlyList<string> recipients, CancellationToken cancellationToken)
    {
        if (recipients.Count is < 1 or > 100) return new(false, "Choose between 1 and 100 students.");
        if (!MailAddress.TryCreate(senderEmail, out var senderAddress) ||
            !recipients.All(x => MailAddress.TryCreate(x, out var parsed) &&
                                 string.Equals(x, parsed.Address, StringComparison.OrdinalIgnoreCase)))
            return new(false, "A sender or student email address is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "Text", content = body },
                toRecipients = new[] { new { emailAddress = new { address = senderAddress.Address } } },
                bccRecipients = recipients.Select(x => new { emailAddress = new { address = x } }).ToArray()
            },
            saveToSentItems = true
        });
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Accepted) return new(true, null);
            logger.LogWarning("Graph rejected faculty email with status {StatusCode}.", (int)response.StatusCode);
            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new(false, "Microsoft did not authorize this mailbox. Try Microsoft sign-in again; if it continues, contact Nelson IT.", true)
                : new(false, "Microsoft did not accept the message. No confirmation of delivery was received.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Graph mail request failed for faculty sender.");
            return new(false, "Microsoft mail could not be reached. Please try again later.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Graph mail request timed out for faculty sender.");
            return new(false, "Microsoft mail timed out. Check Sent Items before trying again, as delivery status is uncertain.");
        }
    }
}

public sealed record FacultyMailSendResult(bool Accepted, string? Error, bool AuthorizationFailed = false);
