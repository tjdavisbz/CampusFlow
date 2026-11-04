using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.CourseSelections;

public static class CourseReviewReconciliation
{
    public static List<CourseReview> KeepActivePending(IEnumerable<CourseReview> reviews,
        IReadOnlySet<string> activeRegistrationIds) => reviews
        .Where(review => !review.NeedsReview || activeRegistrationIds.Contains(review.ExternalCourseRegistrationId))
        .ToList();
}
