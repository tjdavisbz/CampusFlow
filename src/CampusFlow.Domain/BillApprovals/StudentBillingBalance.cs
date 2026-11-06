using System.Collections.Generic;
using System.Linq;
using CampusFlow.StudentInformationSystems;

namespace CampusFlow.BillApprovals;

public sealed record StudentBillingBalance(decimal ThroughSelectedTerm, decimal Overall)
{
    public static StudentBillingBalance Calculate(IEnumerable<StudentBillingTransaction> transactions, string selectedTermCode)
    {
        var active = transactions.Where(x => !x.IsVoided).ToArray();
        return new(active.Where(x => string.CompareOrdinal(x.TermCode, selectedTermCode) <= 0).Sum(x => x.BalanceChange),
            active.Sum(x => x.BalanceChange));
    }
}
