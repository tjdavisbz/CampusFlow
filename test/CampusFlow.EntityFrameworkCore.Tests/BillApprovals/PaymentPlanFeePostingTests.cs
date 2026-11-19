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
    [Fact]
    public void Only_an_open_term_based_cashier_batch_can_receive_fees()
    {
        Assert.True(ElementsPaymentPlanFeeService.ValidateBatch("Payment Plan Fees", "Open", 2, true));
        Assert.False(ElementsPaymentPlanFeeService.ValidateBatch("Payment Plan Fees", "Closed", 2, true));
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateBatch("Housing Charges", "Open", 2, true));
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateBatch("Payment Plan Fees", "Open", 15, true));
        Assert.Throws<InvalidOperationException>(() => ElementsPaymentPlanFeeService.ValidateBatch("Payment Plan Fees", "Open", 2, false));
    }
}
