using System;
using System.Linq;
using CampusFlow.Payments;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.Domain.Tests.StudentInformationSystems;

public class PaymentPlanFeeTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(125.50)]
    [InlineData(0)]
    public void Configured_fee_is_included_exactly_once_in_balance_and_installments(double fee)
    {
        var policy=new StudentPaymentPlanPolicy((decimal)fee,3,3500,1500,new[]{"Residential Undergraduate"},
            new[]{"September 30","October 30"},new[]{"February 28","March 30"},new[]{"July 15"});
        var plan=StudentPaymentPlanCalculator.Calculate(5000,1000,"Spring 2027",new("Full Time","Residential Undergraduate",false),policy);
        Assert.Equal((decimal)fee,plan.PaymentPlanFee);Assert.Equal(5000m+(decimal)fee,plan.Balance);
        Assert.Equal(4000m+(decimal)fee,plan.RemainingBalance);Assert.Equal(plan.RemainingBalance,plan.Installments.Sum(x=>x.Amount));
    }

    [Fact]
    public void A_payment_retains_its_original_posting_term()
    {
        var payment=new PayflowPayment(Guid.NewGuid(),null,Guid.NewGuid(),Guid.NewGuid(),"10",50,0,"USD","request",true);
        payment.SetElementsTerm(269);payment.SetElementsTerm(269);
        Assert.Equal(269,payment.ElementsTermId);
        Assert.Throws<InvalidOperationException>(()=>payment.SetElementsTerm(270));
        Assert.Throws<ArgumentOutOfRangeException>(()=>payment.SetElementsTerm(0));
    }
}
