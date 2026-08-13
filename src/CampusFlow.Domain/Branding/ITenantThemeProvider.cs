using System.Threading.Tasks;

namespace CampusFlow.Branding;

public interface ITenantThemeProvider
{
    Task<TenantTheme> GetAsync(string? tenantName);
}
