using System.Threading.Tasks;
using CampusFlow.Branding;
using Microsoft.Extensions.Configuration;
using Volo.Abp.DependencyInjection;

namespace CampusFlow.Web.Branding;

public sealed class ConfigurationTenantThemeProvider : ITenantThemeProvider, ITransientDependency
{
    private readonly IConfiguration _configuration;

    public ConfigurationTenantThemeProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<TenantTheme> GetAsync(string? tenantName)
    {
        var section = _configuration.GetSection($"TenantThemes:{tenantName ?? "Default"}");
        var fallback = _configuration.GetSection("TenantThemes:Default");

        string Value(string key, string defaultValue) =>
            section[key] ?? fallback[key] ?? defaultValue;

        return Task.FromResult(new TenantTheme(
            Value("OrganizationName", tenantName ?? "CampusFlow"),
            Value("PrimaryColor", "#274690"),
            Value("PrimaryDarkColor", "#172554"),
            Value("AccentColor", "#667eea"),
            Value("NeutralColor", "#a1a8ae"),
            Value("SurfaceColor", "#f7f8fa"),
            Value("TextColor", "#172033")));
    }
}
