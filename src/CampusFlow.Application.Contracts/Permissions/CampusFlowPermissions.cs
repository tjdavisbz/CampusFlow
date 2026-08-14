namespace CampusFlow.Permissions;

public static class CampusFlowPermissions
{
    public const string GroupName = "CampusFlow";

    public static class AdvisorPortal
    {
        public const string Default = GroupName + ".AdvisorPortal";
        public const string ViewAll = Default + ".ViewAll";
        public const string ManageRouting = Default + ".ManageRouting";
    }
}
