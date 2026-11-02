using System;
using System.Collections.Generic;

namespace CampusFlow.CourseSelections;

public sealed record CourseSelectionGroupWindow(
    IReadOnlyList<string> CollegeLevels,
    IReadOnlyList<string> AttendanceTypes,
    DateTime OpensAt,
    DateTime ClosesAt);
