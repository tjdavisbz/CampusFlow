using CampusFlow.StudentInformationSystems;using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.StudentInformationSystems;
public class FinancialAidLoanFlagTests
{
    [Theory] [InlineData("Yes",true)] [InlineData(" True ",true)] [InlineData("1",true)] [InlineData("No",false)] [InlineData("false",false)] [InlineData("0",false)] [InlineData(null,null)] [InlineData("unknown",null)]
    public void Unknown_loan_metadata_never_defaults_to_nonloan(string? input,bool? expected)=>Assert.Equal(expected,ThesisElementsFinancialAidLookup.ReadLoanFlag(input));
    [Fact] public void Native_bit_flags_are_supported(){Assert.True(ThesisElementsFinancialAidLookup.ReadLoanFlag(true));Assert.False(ThesisElementsFinancialAidLookup.ReadLoanFlag(false));}
}
