using System;
using System.Data;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;

public class FinancialAidDistributionAmountTests
{
    [Theory]
    [InlineData(989.43)]
    [InlineData(0d)]
    [InlineData(null)]
    public void Lookup_preserves_gross_and_stored_distribution_amount_separately(double? net)
    {
        using var table = new DataTable();
        for (var i = 0; i < 15; i++) table.Columns.Add("Field" + i, typeof(object));
        table.Rows.Add(123, 269, "B27C", "Spring 2027", new DateTime(2027, 1, 26),
            "Loan", "Anticipated Aid", "Unsubsidized loan", 1000m, "No", DBNull.Value,
            DBNull.Value, true, false, net.HasValue ? (object)(decimal)net.Value : DBNull.Value);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());

        var award = ThesisElementsFinancialAidLookup.ReadAward(reader);

        Assert.Equal(1000m, award.Amount);
        Assert.Equal(net.HasValue ? (decimal?)net.Value : null, award.DistributionAmount);
        Assert.True(award.IsLoan);
        Assert.Null(award.StudentAccepted);
    }
}
