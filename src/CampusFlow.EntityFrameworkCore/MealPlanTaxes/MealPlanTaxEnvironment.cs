using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
namespace CampusFlow.MealPlanTaxes;
internal static class MealPlanTaxEnvironment
{
    internal const string ApiPath="StudentInformationSystems:Providers:ThesisElements:Api:";
    internal const string BatchComment="CampusFlow meal-plan sales tax";
    internal static void Validate(IConfiguration config)
    {
        var np=string.Equals(config["App:EnvironmentLabel"],"NON-PRODUCTION",StringComparison.OrdinalIgnoreCase);
        var prod=string.IsNullOrWhiteSpace(config["App:EnvironmentLabel"]) || string.Equals(config["App:EnvironmentLabel"],"PRODUCTION",StringComparison.OrdinalIgnoreCase);
        if(!config.GetValue<bool>("MealPlanTax:Enabled") || !np&&(!prod||config["ASPNETCORE_ENVIRONMENT"]!="Production") ||
            !config.GetValue<bool>(np?"MealPlanTax:EnableNonProductionWrites":"MealPlanTax:EnableProductionWrites") ||
            config.GetValue<bool>(np?"MealPlanTax:EnableProductionWrites":"MealPlanTax:EnableNonProductionWrites") ||
            !Host(config[ApiPath+"BaseUrl"],np?"app-elm-nonprod-usc-integrationapi.azurewebsites.net":"app-elm-prod-usc-integrationapi.azurewebsites.net") ||
            !Host(config[ApiPath+"RegistrationBaseUrl"],np?"np-usc-gateway.thesiscloud.com":"el-usc-gateway.thesiscloud.com") ||
            config[ApiPath+"TenantHost"]!=(np?"el-np-nel.thesiscloud.com":"el-nel.thesiscloud.com"))
            throw new InvalidOperationException("Meal-plan tax requires explicitly enabled matching Elements environments.");
        var b=new SqlConnectionStringBuilder(config.GetConnectionString("ThesisElementsReadOnly")??throw new InvalidOperationException("The tax reporting connection is missing."));
        var host=np?"sql-elm-nonprod-usc-replica.database.windows.net":"sql-elm-prod-usc-replica.database.windows.net";
        if(!(string.Equals(b.DataSource,host,StringComparison.OrdinalIgnoreCase)||string.Equals(b.DataSource,$"tcp:{host},1433",StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(b.InitialCatalog,np?"EL-NP-NEL-DB":"EL-NEL-DB",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The meal-tax reporting replica does not match.");
    }
    private static bool Host(string? value,string host)=>Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host==host&&u.IsDefaultPort&&u.AbsolutePath=="/"&&u.UserInfo==""&&u.Query==""&&u.Fragment=="";
}
