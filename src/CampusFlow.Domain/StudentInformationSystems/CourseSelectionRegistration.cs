using System;

namespace CampusFlow.StudentInformationSystems;

public sealed record CourseSelectionRegistration(
    StudentInformationSystemProvider Provider,
    string ExternalRegistrationId,
    string ExternalOfferingId,
    string ExternalTermId,
    string Department,
    string CourseCode,
    string CourseType,
    string Section,
    string CourseName,
    decimal Credits,
    string RegistrationStatus,
    DateTime? EffectiveAddDate,
    DateTime? EffectiveWithdrawDate);
