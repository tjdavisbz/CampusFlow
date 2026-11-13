using System;
using System.Collections.Generic;

namespace CampusFlow.Housing;

public class HousingPeriodOption
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class HousingPeriodSetup
{
    public int PeriodId { get; set; }
    public string PeriodName { get; set; } = "";
    public int MasterCommuterRoomCount { get; set; }
    public int PeriodCommuterRoomCount { get; set; }
    public List<string> MissingCommuterRooms { get; set; } = [];
}

public class HousingStudentOption
{
    public int StudentUid { get; set; }
    public string StudentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string ApplicantStatus { get; set; } = "";
    public string Campus { get; set; } = "";
    public string CurrentRoom { get; set; } = "";
}

public class StudentHousingAssignment
{
    public string PeriodName { get; set; } = "";
    public string Building { get; set; } = "";
    public string Room { get; set; } = "";
    public List<string> Roommates { get; set; } = [];
}

public class HousingWorkspaceRoom
{
    public int Id { get; set; }
    public string Building { get; set; } = "";
    public string Name { get; set; } = "";
    public int Capacity { get; set; }
    public List<HousingWorkspaceOccupant> Occupants { get; set; } = [];
}

public class HousingWorkspaceOccupant
{
    public int StudentUid { get; set; }
    public string Name { get; set; } = "";
    public bool IntentionalSingle { get; set; }
    public string Rate { get; set; } = "Unreviewed";
    public string Notes { get; set; } = "";
}

public sealed record HousingCapacityChange(int RoomId, int PreviousCapacity, int Capacity);

public class HousingWorkspaceState
{
    public Guid Id { get; set; }
    public string Revision { get; set; } = "";
    public int PeriodId { get; set; }
    public DateTime RefreshedAt { get; set; }
    public string? ApplyStatus { get; set; }
    public Guid? ApplyRunId { get; set; }
    public string? ApplyFailure { get; set; }
    public bool RecoveryAvailable { get; set; }
    public Guid? RecoveryJobId { get; set; }
    public string? RecoveryJobStatus { get; set; }
    public string? RecoveryJobMessage { get; set; }
    public int RecoveryAssignmentsVerified { get; set; }
    public List<HousingWorkspaceRoom> Rooms { get; set; } = [];
}

public class HousingWorkspaceChange
{
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class ApplyHousingDraftInput
{
    public int PeriodId { get; set; }
    public string Revision { get; set; } = "";
    public string RateRevision { get; set; } = "";
    public bool Confirmed { get; set; }
}

public class HousingRecoveryPreview
{
    public int PeriodId { get; set; }
    public Guid RunId { get; set; }
    public string Revision { get; set; } = "";
    public string RecoveryRevision { get; set; } = "";
    public int SavedAssignments { get; set; }
    public int AlreadyAssigned { get; set; }
    public int RemainingAssignments { get; set; }
    public int HistoricalAssignmentsToClear { get; set; }
    public string Failure { get; set; } = "";
}

public class RecoverHousingDraftInput
{
    public int PeriodId { get; set; }
    public Guid RunId { get; set; }
    public string Revision { get; set; } = "";
    public string RecoveryRevision { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool Confirmed { get; set; }
}

public class HousingTermCopyPreview
{
    public int SourcePeriodId { get; set; }
    public int DestinationPeriodId { get; set; }
    public string SourcePeriodName { get; set; } = "";
    public string DestinationPeriodName { get; set; } = "";
    public int StudentsToCopy { get; set; }
    public int DestinationRooms { get; set; }
    public int IntentionalSinglesToReview { get; set; }
    public List<HousingTermCopyFeeAmount> FeesToCopy { get; set; } = [];
    public string FeeRevision { get; set; } = "";
    public string CopyRevision { get; set; } = "";
    public string Warning { get; set; } = "";
}

public class HousingTermCopyFeeAmount
{
    public string Building { get; set; } = "";
    public string RateChoice { get; set; } = "";
    public decimal BaseAmount { get; set; }
    public decimal SingleSurchargeAmount { get; set; }
}

public class CopyHousingTermInput
{
    public int SourcePeriodId { get; set; }
    public int DestinationPeriodId { get; set; }
    public int ExpectedStudents { get; set; }
    public string ExpectedFeeRevision { get; set; } = "";
    public string ExpectedCopyRevision { get; set; } = "";
    public bool Confirmed { get; set; }
}
