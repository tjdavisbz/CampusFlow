using System;

namespace CampusFlow.StudentInformationSystems;

public sealed class CourseRegistrationValidationException : Exception
{
    public CourseRegistrationValidationException(string message) : base(message) { }
}
