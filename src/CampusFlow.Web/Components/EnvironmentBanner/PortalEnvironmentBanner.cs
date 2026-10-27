using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;

namespace CampusFlow.Web.Components.EnvironmentBanner;

public static class PortalEnvironmentBanner
{
    public const string ConfigurationKey = "App:EnvironmentLabel";

    public static string Render(IConfiguration? configuration)
    {
        var label = configuration?[ConfigurationKey]?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var encodedLabel = HtmlEncoder.Default.Encode(label);
        return $"""
            <aside class="portal-environment-banner" aria-label="Portal environment">
                <strong>{encodedLabel}</strong>
                <span>For testing only. Use the production portal for official activity.</span>
            </aside>
            """;
    }
}
