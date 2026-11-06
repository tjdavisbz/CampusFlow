using System;
using CampusFlow.BillApprovals;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.Domain.Tests.BillApprovals;

public class StudentBillingBalanceTests
{
    private static StudentBillingTransaction Row(string term, decimal amount, bool pending=false, bool voided=false) =>
        new(StudentInformationSystemProvider.ThesisElements,Guid.NewGuid().ToString(),null,term,term,DateTime.Today,"Activity",
            Math.Max(amount,0),Math.Max(-amount,0),amount,pending,voided);

    [Fact]
    public void Current_balance_excludes_future_activity_but_retains_prior_balances_and_pending_items()
    {
        var rows=new[]{Row("B25Q",157),Row("B26Q",50,true),Row("B26Q",-10),Row("B26Q",999,false,true),Row("B27S",3380,true)};
        var balance=StudentBillingBalance.Calculate(rows,"B26Q");
        Assert.Equal(197m,balance.ThroughSelectedTerm);Assert.Equal(3577m,balance.Overall);
        Assert.Equal(3577m,StudentBillingBalance.Calculate(rows,"B27S").ThroughSelectedTerm);
    }

    [Fact]
    public void A_zero_current_balance_does_not_default_to_future_charges()
    {
        var balance=StudentBillingBalance.Calculate(new[]{Row("B26Q",0),Row("B27S",3380)},"B26Q");
        Assert.Equal(0m,balance.ThroughSelectedTerm);Assert.Equal(3380m,balance.Overall);
    }
}
