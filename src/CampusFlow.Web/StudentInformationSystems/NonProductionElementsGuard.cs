using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.StudentInformationSystems;

/// <summary>
/// Prevents the Staging portal from falling back to the production Elements defaults in
/// appsettings.json when an App Service override is absent or accidentally changed.
/// </summary>
public static class NonProductionElementsGuard
{
    private const string ApiPrefix = "StudentInformationSystems:Providers:ThesisElements:Api:";
    private const string IntegrationHost = "app-elm-nonprod-usc-integrationapi.azurewebsites.net";
    private const string RegistrationHost = "np-usc-gateway.thesiscloud.com";
    private const string TenantHost = "el-np-nel.thesiscloud.com";
    private const string ReplicaHost = "sql-elm-nonprod-usc-replica.database.windows.net";
    private const string ReplicaDatabase = "EL-NP-NEL-DB";

    public static void Validate(string environmentName, IConfiguration configuration)
    {
        if (!string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase)) return;

        RequireHost(configuration[ApiPrefix + "BaseUrl"], IntegrationHost, "Elements integration API");
        RequireHost(configuration[ApiPrefix + "RegistrationBaseUrl"], RegistrationHost,
            "Elements registration gateway");
        if (!string.Equals(configuration[ApiPrefix + "TenantHost"]?.TrimEnd('/'), TenantHost,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staging Elements tenant host is not the approved non-production host.");

        var connectionString = configuration.GetConnectionString("ThesisElementsReadOnly");
        SqlConnectionStringBuilder connection;
        try { connection = new SqlConnectionStringBuilder(connectionString); }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Staging Elements replica connection string is invalid.", exception);
        }
        var dataSource = connection.DataSource.Trim();
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            dataSource = dataSource[4..];
        var host = dataSource.Split(',', 2)[0].Trim();
        if (!string.Equals(host, ReplicaHost, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.InitialCatalog, ReplicaDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staging Elements replica is not the approved non-production database.");
    }

    private static void RequireHost(string? url, string expectedHost, string destination)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.DnsSafeHost, expectedHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException($"Staging {destination} is not the approved non-production endpoint.");
    }
}
