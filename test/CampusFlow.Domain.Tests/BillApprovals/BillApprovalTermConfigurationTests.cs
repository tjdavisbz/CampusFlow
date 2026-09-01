using System;
using Shouldly;
using Xunit;

namespace CampusFlow.BillApprovals;

public class BillApprovalTermConfigurationTests
{
    [Fact]
    public void Disabled_draft_is_not_open_inside_its_window()
    {
        var config = Create(false);
        config.IsOpen(new DateTime(2026, 8, 15)).ShouldBeFalse();
    }

    [Fact]
    public void Enabled_term_is_open_only_inside_its_window()
    {
        var config = Create(true);
        config.IsOpen(new DateTime(2026, 7, 31)).ShouldBeFalse();
        config.IsOpen(new DateTime(2026, 8, 15)).ShouldBeTrue();
        config.IsOpen(new DateTime(2026, 9, 1)).ShouldBeFalse();
    }

    private static BillApprovalTermConfiguration Create(bool enabled) => new(Guid.NewGuid(), Guid.NewGuid(),
        "400", "B26Q", "Fall 2026", new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
        enabled, Guid.NewGuid(), Guid.NewGuid());
}
