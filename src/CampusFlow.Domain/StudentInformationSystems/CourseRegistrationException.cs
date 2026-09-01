using System;

namespace CampusFlow.StudentInformationSystems;

public sealed class CourseRegistrationException : Exception
{
    public bool ExternalRegistrationMayHaveCompleted { get; }

    public CourseRegistrationException(string message, bool externalRegistrationMayHaveCompleted,
        Exception innerException) : base(message, innerException)
    {
        ExternalRegistrationMayHaveCompleted = externalRegistrationMayHaveCompleted;
    }
}
