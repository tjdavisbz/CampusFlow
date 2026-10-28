using System;
using System.Collections.Generic;
using CampusFlow.Housing;
using Xunit;

namespace CampusFlow.Domain.Tests.Housing;

public class HousingChargeCalculatorTests
{
    private static readonly IReadOnlyCollection<HousingTermFee> Fees =
    [
        new("Bridges/Teeter", 137, 2500m),
        new("Savell", 137, 1700m),
        new("Savell Triple Occupancy", 1765, 1525m),
        new("Gannon", 1471, 3950m),
        new("AIC Triple Room", 1471, 3250m)
    ];
    private static readonly IReadOnlyCollection<HousingRateAmount> Rates =
    [
        new("Bridges Hall", "Standard", 2500m, 2125m),
        new("Savell Hall", "Standard", 1700m, 1445m),
        new("Savell Hall", "Savell Triple Occupancy", 1525m, 1296.25m),
        new("Theodore Gannon Dormitory", "Standard", 3950m, 1827.50m),
        new("Theodore Gannon Dormitory", "AIC Triple Room", 3250m, 1232.50m)
    ];

    [Fact]
    public void Standard_Housing_Uses_CampusFlow_Amount_Not_Elements_Fee_Amount()
    {
        var metadata = new List<HousingTermFee> { new("Bridges/Teeter", 137, 9999m) };
        var lines = HousingChargeCalculator.Quote(268, "Bridges Hall", "Standard", false, metadata, Rates, 243);
        Assert.Single(lines);
        Assert.Equal(2500m, lines[0].Amount);
        Assert.Equal(137, lines[0].TransactionDocumentId);
    }

    [Fact]
    public void Intentional_Single_Is_Separate_Charge()
    {
        var lines = HousingChargeCalculator.Quote(268, "Bridges Hall", "Standard", true, Fees, Rates, 243);
        Assert.Equal(2, lines.Count);
        Assert.Equal(2125m, lines[1].Amount);
        Assert.Equal(243, lines[1].TransactionDocumentId);
    }

    [Fact]
    public void Aic_Single_Uses_Legacy_Exclusion_And_Custom_Base()
    {
        var lines = HousingChargeCalculator.Quote(268, "Theodore Gannon Dormitory", "AIC Triple Room", true, Fees, Rates, 243);
        Assert.Equal(3250m, lines[0].Amount);
        Assert.Equal(1232.50m, lines[1].Amount);
    }

    [Fact]
    public void Savell_Triple_Uses_Custom_Fee()
    {
        var lines = HousingChargeCalculator.Quote(268, "Savell Hall", "Savell Triple Occupancy", false, Fees, Rates, 243);
        Assert.Single(lines);
        Assert.Equal(1525m, lines[0].Amount);
        Assert.Equal(1765, lines[0].TransactionDocumentId);
    }

    [Theory]
    [InlineData(269, "Bridges Hall", "Standard")]
    [InlineData(268, "Bridges Hall", "AIC Triple Room")]
    [InlineData(268, "Savell Hall", "Unreviewed")]
    public void Unreviewed_Term_Or_Mapping_Is_Rejected(int term, string building, string rate) =>
        Assert.Throws<ArgumentException>(() => HousingChargeCalculator.Quote(term, building, rate, false, Fees, Rates, 243));

    [Fact]
    public void Missing_CampusFlow_Rate_Is_Rejected() =>
        Assert.Throws<ArgumentException>(() => HousingChargeCalculator.Quote(268, "Bridges Hall", "Standard", false, Fees, [], 243));
}
