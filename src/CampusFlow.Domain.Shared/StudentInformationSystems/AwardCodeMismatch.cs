namespace CampusFlow.StudentInformationSystems;

public record AwardCodeMismatch(int AwardId, int StudentUid, string StudentId, string StudentName,
    string Term, string Description, int? CurrentCodeId, int? ReferenceCodeId,
    bool CurrentShowOnBill, bool ReferenceShowOnBill);
