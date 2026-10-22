namespace CampusFlow.Web.Maintenance;

public sealed class MaintenanceModeOptions
{
    public const string SectionName = "MaintenanceMode";

    public bool Enabled { get; set; }

    public string Description { get; set; } =
        "CampusFlow is temporarily unavailable while scheduled maintenance is completed. Please check back soon.";
}
