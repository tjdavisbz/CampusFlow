using System;
namespace CampusFlow.StudentInformationSystems;

public static class FinancialAidRules
{
    public static bool CanDecide(StudentFinancialAidAward award) => award.Amount > 0 &&
        !award.IsSentToBilling && !award.IsLocked && award.StudentAccepted is null &&
        !award.AwardStatus.Contains("Cancel", StringComparison.OrdinalIgnoreCase);

    public static bool IncludeInBillApproval(StudentFinancialAidAward award)
    {
        if (award.Amount <= 0 || award.IsSentToBilling || award.StudentAccepted == 0 ||
            award.AwardStatus.Contains("Cancel", StringComparison.OrdinalIgnoreCase)) return false;
        if (award.Provider != StudentInformationSystemProvider.ThesisElements) return true;
        var type = award.AwardType.Trim();
        if (type.Equals("FWS", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("TWWS", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("TX MENTOR", StringComparison.OrdinalIgnoreCase)) return false;
        if (type.Equals("TEG", StringComparison.OrdinalIgnoreCase) &&
            !award.AwardStatus.Trim().Equals("Guarantee", StringComparison.OrdinalIgnoreCase)) return false;
        // Missing loan metadata must never make an unaccepted loan count as anticipated aid.
        return award.IsLoan is false || award.StudentAccepted == 1;
    }
}
