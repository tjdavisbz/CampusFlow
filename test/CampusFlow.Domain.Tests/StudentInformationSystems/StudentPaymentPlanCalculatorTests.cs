using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace CampusFlow.StudentInformationSystems;

public class StudentPaymentPlanCalculatorTests
{
    private static readonly StudentPaymentPlanPolicy Policy = new(
        100m, 3m, 3500m, 1500m,
        new[] { "Residential Undergraduate" },
        new[] { "September 30", "October 30", "November 30", "December 30" },
        new[] { "February 28", "March 30", "April 30", "May 30" },
        new[] { "July 15", "August 15" });

    [Fact]
    public void Should_apply_residential_minimum_and_split_the_remainder()
    {
        var plan = StudentPaymentPlanCalculator.Calculate(
            10_000m, 2_000m, "Fall 2026",
            new StudentPaymentPlanContext("Full Time", "Residential Undergraduate", false), Policy);

        plan.Balance.ShouldBe(10_100m);
        plan.Installments.Count.ShouldBe(5);
        plan.Installments[0].Amount.ShouldBe(1_500m);
        plan.Installments.ShouldAllBe(x => x.Amount >= 0m);
        plan.Installments.Sum(x => x.Amount).ShouldBe(8_100m);
    }

    [Fact]
    public void Should_use_three_payments_for_summer_and_preserve_cents()
    {
        var plan = StudentPaymentPlanCalculator.Calculate(
            3_412.01m, 0m, "Summer 2026",
            new StudentPaymentPlanContext("Part Time", "Distance Education", true), Policy);

        plan.Installments.Count.ShouldBe(3);
        plan.Installments.Sum(x => x.Amount).ShouldBe(plan.RemainingBalance);
        plan.Installments[1].DueDescription.ShouldBe("July 15");
    }
}
