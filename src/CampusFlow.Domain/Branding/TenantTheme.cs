namespace CampusFlow.Branding;

public sealed record TenantTheme(
    string OrganizationName,
    string PrimaryColor,
    string PrimaryDarkColor,
    string AccentColor,
    string NeutralColor,
    string SurfaceColor,
    string TextColor);
