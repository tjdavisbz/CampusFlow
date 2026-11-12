using System;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.Pages;

public sealed class FinancialAidDisplayTests
{
    private static StudentFinancialAidAward Award(decimal? net) =>
        new(StudentInformationSystemProvider.ThesisElements, "1", "269", "2027S", "Spring 2027",
            DateTime.Today, "UNSUB", "Anticipated", "Loan", 1000m, false, null, null,
            IsLoan: true, DistributionAmount: net);

    [Fact]
    public void Display_uses_net_without_changing_award_decisions_or_gross_amount()
    {
        var award = Award(989.43m);
        Assert.Equal(989.43m, FinancialAidDisplay.Amount(award));
        Assert.Equal(1000m, award.Amount);
        Assert.True(FinancialAidRules.CanDecide(award));
        Assert.True(FinancialAidRules.IsLoanAwaitingAcceptance(award));
        Assert.False(FinancialAidRules.IncludeInBillApproval(award));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Missing_or_invalid_net_is_not_substituted_with_gross_or_a_partial_total(int? net)
    {
        var award = Award(net);
        Assert.Null(FinancialAidDisplay.Amount(award));
        Assert.Null(FinancialAidDisplay.Total([Award(989.43m), award]));
        Assert.Equal("Amount unavailable", FinancialAidDisplay.Currency(FinancialAidDisplay.Amount(award)));
        Assert.True(FinancialAidRules.CanDecide(award));
    }

    [Fact]
    public void Zero_is_a_real_net_amount_and_totals_match_cards()
    {
        Assert.Equal(0m, FinancialAidDisplay.Amount(Award(0m)));
        Assert.Equal(989.43m, FinancialAidDisplay.Total([Award(0m), Award(989.43m)]));
        Assert.Equal(0m, FinancialAidDisplay.Total([]));
    }
}
