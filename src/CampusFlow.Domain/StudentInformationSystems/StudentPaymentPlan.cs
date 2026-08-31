using System.Collections.Generic;
using System.Linq;

namespace CampusFlow.StudentInformationSystems;

public sealed record StudentPaymentPlan(
    decimal Balance,
    decimal TotalCredits,
    decimal RemainingBalance,
    string FullTimeStatus,
    string AttendanceType,
    bool IsCommuter,
    IReadOnlyList<StudentPaymentInstallment> Installments);

public sealed record StudentPaymentInstallment(int Number, decimal Amount, string DueDescription);

public sealed record StudentPaymentPlanContext(
    string FullTimeStatus,
    string AttendanceType,
    bool IsCommuter);

public sealed record StudentPaymentPlanPolicy(
    decimal EnrollmentFee,
    decimal PartTimeBalanceDivisor,
    decimal ResidentialMinimumPayment,
    decimal StandardMinimumPayment,
    IReadOnlyCollection<string> ResidentialAttendanceTypes,
    IReadOnlyList<string> FallDueDates,
    IReadOnlyList<string> SpringDueDates,
    IReadOnlyList<string> SummerDueDates);

public static class StudentPaymentPlanCalculator
{
    public static StudentPaymentPlan Calculate(
        decimal accountBalance,
        decimal anticipatedAid,
        string termName,
        StudentPaymentPlanContext context,
        StudentPaymentPlanPolicy policy)
    {
        var balance = accountBalance + policy.EnrollmentFee;
        var remaining = balance - anticipatedAid;
        var dates = SelectDates(termName, policy);
        var paymentCount = dates.Count + 1;
        var isPartTime = !string.Equals(context.FullTimeStatus, "Full Time", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(context.AttendanceType, "Dual Credit", System.StringComparison.OrdinalIgnoreCase);
        var minimum = isPartTime
            ? decimal.Round(balance / policy.PartTimeBalanceDivisor, 2, System.MidpointRounding.AwayFromZero)
            : policy.ResidentialAttendanceTypes.Contains(context.AttendanceType) && !context.IsCommuter
                ? policy.ResidentialMinimumPayment
                : policy.StandardMinimumPayment;

        var first = anticipatedAid >= minimum ? 0m : minimum >= balance ? remaining : minimum - anticipatedAid;
        first = decimal.Ceiling(first * 100m) / 100m;
        var runBalance = balance - anticipatedAid - first;
        var installments = new List<StudentPaymentInstallment>(paymentCount)
        {
            new(1, decimal.Max(0, first), "Registration day")
        };
        for (var index = 0; index < dates.Count; index++)
        {
            var paymentsLeft = dates.Count - index;
            var amount = paymentsLeft == 1
                ? runBalance
                : decimal.Round(runBalance / paymentsLeft, 2, System.MidpointRounding.AwayFromZero);
            amount = decimal.Max(0, amount);
            runBalance -= amount;
            installments.Add(new StudentPaymentInstallment(index + 2, amount, dates[index]));
        }

        return new StudentPaymentPlan(balance, anticipatedAid, remaining, context.FullTimeStatus, context.AttendanceType, context.IsCommuter, installments);
    }

    private static IReadOnlyList<string> SelectDates(string termName, StudentPaymentPlanPolicy policy) =>
        termName.StartsWith("Fall", System.StringComparison.OrdinalIgnoreCase) ? policy.FallDueDates :
        termName.StartsWith("Spring", System.StringComparison.OrdinalIgnoreCase) ? policy.SpringDueDates :
        policy.SummerDueDates;
}
