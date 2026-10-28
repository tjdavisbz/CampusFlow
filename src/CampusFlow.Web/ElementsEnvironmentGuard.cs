using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web;

// A configuration error on the non-production site must never send student reads or writes to production Elements.
public static class ElementsEnvironmentGuard
{
    public static void Validate(IConfiguration configuration)
    {
        if (!string.Equals(configuration["App:EnvironmentLabel"], "NON-PRODUCTION",
                StringComparison.OrdinalIgnoreCase)) return;

        const string prefix = "StudentInformationSystems:Providers:ThesisElements:Api:";
        if (!MatchesHttpsHost(configuration[prefix + "BaseUrl"],
                "app-elm-nonprod-usc-integrationapi.azurewebsites.net") ||
            !MatchesHttpsHost(configuration[prefix + "RegistrationBaseUrl"],
                "np-usc-gateway.thesiscloud.com") ||
            !string.Equals(configuration[prefix + "TenantHost"], "el-np-nel.thesiscloud.com",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The non-production portal is not configured for non-production Elements. Startup was stopped.");

        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The non-production Elements replica connection is missing. Startup was stopped.");
        var replica = new SqlConnectionStringBuilder(connectionString);
        if (!MatchesReplicaHost(replica.DataSource) ||
            !string.Equals(replica.InitialCatalog, "EL-NP-NEL-DB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The non-production portal is not configured for the non-production Elements replica. Startup was stopped.");
    }

    private static bool MatchesHttpsHost(string? value, string expectedHost) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";

    private static bool MatchesReplicaHost(string value) =>
        string.Equals(value, "sql-elm-nonprod-usc-replica.database.windows.net", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "tcp:sql-elm-nonprod-usc-replica.database.windows.net,1433", StringComparison.OrdinalIgnoreCase);
}
