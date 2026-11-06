using System;
using CampusFlow.BillApprovals;
using Volo.Abp.Data;
using Xunit;

namespace CampusFlow.Domain.Tests.BillApprovals;

public class BillApprovalTestAccessTests
{
    private static readonly DateTime Now = new(2026, 11, 5);
    private static BillApprovalTermConfiguration Term(bool enabled = false) => new(Guid.NewGuid(), Guid.NewGuid(),
        "269", "B27S", "Spring 2027", Now.AddDays(1), Now.AddDays(30), enabled, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Test_access_is_off_by_default_and_does_not_open_public_access()
    {
        var term = Term();
        Assert.False(term.TestAccessEnabled);
        term.ConfigureTestAccess(true, new[] { "100", " 100 ", "200" });
        Assert.Equal(2, term.GetTestStudentIds().Count);
        Assert.True(term.IsOpenForStudent(Now, "100"));
        Assert.False(term.IsOpenForStudent(Now, "1000"));
        Assert.False(term.IsOpenForStudent(Now, ""));
        Assert.False(term.IsOpen(Now));
        term.ConfigureTestAccess(false, new[] { "100" });
        Assert.False(term.IsOpenForStudent(Now, "100"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("internal-id")]
    [InlineData("100,200")]
    public void Invalid_test_ids_are_rejected(string id) => Assert.Throws<ArgumentException>(() => Term().ConfigureTestAccess(true, new[] { id }));

    [Fact]
    public void Empty_enabled_list_and_oversized_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Term().ConfigureTestAccess(true, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => Term().ConfigureTestAccess(true, new[] { new string('1', 65) }));
    }

    [Fact]
    public void Malformed_stored_test_list_does_not_grant_access()
    {
        var term = Term();term.ConfigureTestAccess(true, new[] { "100" });
        term.SetProperty("BillApprovalTestStudentIds", "not json");
        Assert.False(term.IsOpenForStudent(Now, "100"));
    }

    [Fact]
    public void Test_access_is_specific_to_the_term()
    {
        var first = Term();var second = Term();first.ConfigureTestAccess(true, new[] { "100" });
        Assert.False(second.IsOpenForStudent(Now, "100"));
    }

    [Fact]
    public void Upcoming_closed_and_disabled_terms_have_distinct_states()
    {
        var term = Term(true);
        Assert.Equal("upcoming", term.Availability(Now, "100"));
        Assert.Equal("open", term.Availability(Now.AddDays(2), "100"));
        Assert.Equal("closed", term.Availability(Now.AddDays(31), "100"));
        Assert.Equal("unavailable", Term().Availability(Now, "100"));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(50, false)]
    [InlineData(50.01, true)]
    public void Previous_balance_threshold_preserves_the_existing_rule(double balance, bool blocked) =>
        Assert.Equal(blocked, BillApprovalAccessRules.HasBlockingPreviousBalance((decimal)balance));
}
