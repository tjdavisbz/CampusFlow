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

    public static class StudentImpersonation
    {
        public const string Default = GroupName + ".StudentImpersonation";
    }

    public static class Admin
    {
        public const string Default = GroupName + ".Admin";
        public const string GlobalConfiguration = Default + ".GlobalConfiguration";
        public const string PaymentPlans = Default + ".PaymentPlans";
        public const string BillApproval = Default + ".BillApproval";
        public const string RegistrationRules = Default + ".RegistrationRules";
        public const string AccessManagement = Default + ".AccessManagement";
    }
}
