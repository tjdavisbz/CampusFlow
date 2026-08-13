namespace CampusFlow.Branding;

public sealed record TenantTheme(
    string OrganizationName,
    string PrimaryColor,
    string PrimaryDarkColor,
    string AccentColor,
    string NeutralColor,
    string SurfaceColor,
    string TextColor,
    string? LogoUrl,
    string? LogoReverseUrl,
    string HeadingFontFamily,
    string BodyFontFamily,
    string? FontStylesheetUrl);
