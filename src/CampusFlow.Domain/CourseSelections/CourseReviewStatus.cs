namespace CampusFlow.CourseSelections;

public enum CourseReviewStatus
{
    Pending,
    Approved,
    RejectionPending,
    Rejected,
    Failed,
    RemovedByStudent
}

public enum ExternalCourseRemovalStatus
{
    NotRequired,
    Pending,
    Completed,
    Failed
}

public enum ReviewEmailStatus
{
    NotConfigured,
    Pending,
    Sent,
    Failed
}
