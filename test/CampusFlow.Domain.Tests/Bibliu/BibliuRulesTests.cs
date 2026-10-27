using System;
using CampusFlow.BillApprovals;
using Shouldly;
using Xunit;
namespace CampusFlow.Bibliu;

public class BibliuRulesTests
{
    [Theory]
    [InlineData(300,0,500)]
    [InlineData(-300,0,500)]
    [InlineData(-500,0,500)]
    [InlineData(-800,200,600)]
    [InlineData(-800,900,0)]
    [InlineData(-800,-200,800)]
    public void Voucher_keeps_the_legacy_floor_and_subtracts_only_positive_usage(decimal balance,decimal used,decimal expected) =>
        BibliuRules.VoucherAmount(balance,used).ShouldBe(expected);
    [Theory]
    [InlineData("Spring 2027","Fall 2026,Spring 2027,Summer 2027")]
    [InlineData("Summer 2027","Spring 2027,Summer 2027,Fall 2027")]
    [InlineData("Fall 2026","Summer 2026,Fall 2026,Spring 2027")]
    public void Expansion_preserves_academic_year_boundaries(string selected,string expected) =>
        string.Join(",",BibliuRules.ExpandedTerms(selected)).ShouldBe(expected);
    [Fact]
    public void Draft_and_awaiting_payment_do_not_qualify()
    {
        var bill=new BillApproval(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"123","456","268","B26Q","Fall 2026");
        BibliuRules.IsApproved(bill).ShouldBeFalse();
        bill.UpdateDraft(BillPaymentChoice.PayNow,100,0,0,100,0,"[]","{}");
        bill.Accept(Guid.NewGuid(),1,"agreement",DateTime.UtcNow,null,null);
        BibliuRules.IsApproved(bill).ShouldBeFalse();
        var accepted=new BillApproval(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"123","456","268","B26Q","Fall 2026");
        accepted.Accept(Guid.NewGuid(),1,"agreement",DateTime.UtcNow,null,null);
        BibliuRules.IsApproved(accepted).ShouldBeTrue();
    }
    [Fact]
    public void Pos_identifiers_and_cents_have_fixed_widths()
    {
        BibliuRules.StudentRecordNumber("123","12345").ShouldBe("1230000000012345");
        BibliuRules.Cents(600.25m).ShouldBe("000000000000060025");
        BibliuRules.Pad("Long name",4).ShouldBe("Long");
        Should.Throw<ArgumentException>(()=>BibliuRules.StudentRecordNumber("123","1E3"));
    }
    [Theory]
    [InlineData(false,-500,"123500")]
    [InlineData(false,-501,"123100")]
    [InlineData(true,-800,"123200")]
    public void Hold_has_priority_over_credit_event(bool hold,decimal balance,string expected) =>
        BibliuRules.EventNumber("123",hold,balance).ShouldBe(expected);
    [Fact]
    public void Config_rejects_invalid_terms_and_preserves_leading_zero_store_numbers()
    {
        var config=new BibliuConfiguration(Guid.NewGuid(),Guid.NewGuid());
        config.Update("268","269","012","024",false);
        config.StoreNumber.ShouldBe("012");
        config.IsEnabled.ShouldBeFalse();
        Should.Throw<ArgumentException>(()=>config.Update("0","269","123","241",true));
        Should.Throw<ArgumentException>(()=>config.Update("268","269","12","241",true));
        Should.Throw<ArgumentException>(()=>BibliuRules.ExpandedTerms("Winter 2027"));
    }
}
