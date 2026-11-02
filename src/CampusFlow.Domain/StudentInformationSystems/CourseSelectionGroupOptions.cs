using System.Collections.Generic;

namespace CampusFlow.StudentInformationSystems;

public sealed record CourseSelectionGroupOptions(IReadOnlyList<string> CollegeLevels,
    IReadOnlyList<string> AttendanceTypes);
