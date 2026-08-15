using System.Collections.Generic;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentDegreeAuditSummary(
    int RevisionTermId,
    int AuditDegreeId,
    int AuditProgramId,
    string Degree,
    string Program,
    string RevisionTerm,
    decimal CreditsRequired,
    decimal CreditsCompleted,
    decimal MinimumGpa,
    decimal GpaAttained,
    string Status,
    bool NeedsUpdate);

public sealed record StudentDegreeAuditCourse(
    string RequirementName,
    decimal RequirementCreditsRequired,
    decimal RequirementCreditsCompleted,
    decimal RequirementMinimumGpa,
    decimal RequirementGpaAttained,
    string RequirementStatus,
    int RequirementSortOrder,
    string GroupName,
    decimal GroupCreditsRequired,
    decimal GroupCreditsCompleted,
    decimal GroupMinimumGpa,
    decimal GroupGpaAttained,
    string GroupStatus,
    int GroupSortOrder,
    string CourseCode,
    string CourseName,
    string CourseStatus,
    string Grade,
    decimal Credits,
    string Term,
    string MatchedCourseCode);

public sealed record StudentDegreeAuditDetail(
    StudentDegreeAuditSummary Summary,
    decimal? CumulativeGpa,
    IReadOnlyList<StudentDegreeAuditCourse> Courses);
