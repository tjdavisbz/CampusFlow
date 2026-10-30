using System;
using CampusFlow.StudentInformationSystems;
using CampusFlow.Web.Pages;
using Xunit;

namespace CampusFlow.Pages;

public sealed class FinancialAidVisibilityTests
{
    [Fact]
    public void Student_page_excludes_zero_dollar_awards()
    {
        var awards = new[] { Award("zero", 0m), Award("nonzero", 100m) };

        var visible = FinancialAidModel.StudentVisibleAwards(awards);

        Assert.Equal("nonzero", Assert.Single(visible).ExternalAwardId);
    }

    private static StudentFinancialAidAward Award(string id, decimal amount) =>
        new(StudentInformationSystemProvider.ThesisElements, id, "268", "2026F", "Fall 2026",
            DateTime.Today, "Scholarship", "Awarded", "Test award", amount, false, null, null);
}
