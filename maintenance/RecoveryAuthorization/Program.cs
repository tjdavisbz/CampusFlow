using CampusFlow.AccountRecovery;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

// Run only from a matching private operator or App Service maintenance directory. It never
// calls a student reset endpoint, accepts a student password, or enables recovery.
if (args.Length is not (3 or 5) || args[0] is not ("authorize" or "verify") || args[1] != "--environment" || args[2] is not ("nonprod" or "production") ||
    args.Length == 5 && args[3] != "--cache-root")
{ Console.Error.WriteLine("Usage: authorize|verify --environment nonprod|production [--cache-root PRIVATE_ABSOLUTE_DIRECTORY]"); return 2; }
var environment = args[2];
var config = new ConfigurationBuilder().AddEnvironmentVariables("APPSETTING_").AddEnvironmentVariables().Build();
var actualEnvironment = string.Equals(config["App:EnvironmentLabel"], "NON-PRODUCTION", StringComparison.OrdinalIgnoreCase) ? "nonprod" : "production";
if (actualEnvironment != environment || config.GetValue<bool>("PasswordRecovery:Enabled"))
{ Console.Error.WriteLine("Use the matching environment with recovery disabled during authorization."); return 2; }
var credential = args.Length == 5 ? DelegatedRecoveryCredential.ForOperator(config, args[4]) : new DelegatedRecoveryCredential(config);
if (!credential.AuthorizationConfigured)
{ Console.Error.WriteLine("Recovery authorization settings are incomplete."); return 2; }
try
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(16));
    if (args[0] == "verify")
    {
        await credential.VerifyAuthorizationAsync(deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { authorized = true, authorizationReused = true, passwordResetSubmitted = false, recoveryEnabled = false }));
        return 0;
    }
    await credential.AuthorizeAsync(result =>
    {
        // Only the short-lived code and official Microsoft URL reach the operator.
        // No access/refresh token, password, or full authentication exception is printed.
        if (!Uri.TryCreate(result.VerificationUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            !(uri.Host == "login.microsoft.com" && uri.AbsolutePath == "/device" ||
              uri.Host is "microsoft.com" or "www.microsoft.com" && uri.AbsolutePath == "/devicelogin"))
            throw new InvalidOperationException("Unexpected Microsoft authorization destination.");
        Console.WriteLine(JsonSerializer.Serialize(new { verificationUrl = result.VerificationUrl, userCode = result.UserCode, expiresOn = result.ExpiresOn }));
        return Task.CompletedTask;
    }, deadline.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { authorized = true, encryptedCacheSaved = true, passwordResetSubmitted = false, recoveryEnabled = false }));
    return 0;
}
catch (Microsoft.Identity.Client.MsalException ex)
{
    var code = ex.ErrorCode.Length <= 80 && ex.ErrorCode.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-') ? ex.ErrorCode : "authorization_failed";
    Console.Error.WriteLine(JsonSerializer.Serialize(new { authorized = false, errorCode = code, passwordResetSubmitted = false }));
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { authorized = false, exceptionType = ex.GetType().Name, passwordResetSubmitted = false }));
    Console.Error.WriteLine("Microsoft authorization did not complete. Sign in with the configured service account using its required MFA. No student password reset was submitted.");
    return 1;
}
