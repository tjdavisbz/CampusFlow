namespace CampusFlow.Branding;

public interface ITenantThemeProvider
{
    TenantTheme Get(string? tenantName);
}
