using System;
using CampusFlow.StudentInformationSystems;
using Xunit;

namespace CampusFlow.EntityFrameworkCore.Tests.BillApprovals;

public class PaymentPlanFeePostingTests
{
    [Fact]
    public void A_single_existing_fee_is_retained_and_not_added_to_the_plan_again()
    {
        Assert.Equal(0m, ElementsPaymentPlanFeeService.ValidateExisting([], 100m));
        Assert.Equal(100m, ElementsPaymentPlanFeeService.ValidateExisting([(100m, 0m)], 100m));
    }

    [Fact]
    public void Duplicate_fees_credits_and_amount_mismatches_require_review()
    {
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateExisting([(100m, 0m), (100m, 0m)], 100m));
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateExisting([(100m, 100m)], 100m));
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateExisting([(50m, 0m)], 100m));
    }
}
