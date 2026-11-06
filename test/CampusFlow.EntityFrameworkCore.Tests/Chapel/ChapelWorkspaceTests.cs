using System;
using System.Linq;
using System.Threading.Tasks;
using CampusFlow.Chapel;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
namespace CampusFlow.EntityFrameworkCore.Tests.Chapel;

[Collection(CampusFlowTestConsts.CollectionDefinitionName)]
public class ChapelWorkspaceTests : CampusFlowEntityFrameworkCoreTestBase
{
    private readonly Guid _tenant = Guid.NewGuid();
    private int _nextStudentUid = 10;
    private readonly IChapelDormLookup _dorms = Substitute.For<IChapelDormLookup>();
    private ChapelAttendanceAppService Service()
    { return new(GetRequiredService<IRepository<ChapelAttendanceRecord, Guid>>(), GetRequiredService<IRepository<ChapelGroupMembership, Guid>>(), GetRequiredService<IUnitOfWorkManager>(), _dorms) { LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>() }; }
    private IDisposable Tenant()
    {
        // Keep the real tenant data filter while supplying the institution name required
        // by the shared Elements dorm lookup.
        return GetRequiredService<ICurrentTenant>().Change(_tenant, "nelson");
    }
    private async Task<Guid> Seed(string studentId = "001", int term = 268)
    {
        var id = Guid.NewGuid(); await WithUnitOfWorkAsync(async () =>
        { await GetRequiredService<IRepository<ChapelAttendanceRecord, Guid>>().InsertAsync(new(id, _tenant, term, _nextStudentUid++, studentId, "Test Student", new DateTime(2026, 10, 1, 9, 30, 0), ChapelAttendanceStatus.Absent, "Scan In Only"), autoSave: true); }); return id;
    }
    [Fact]
    public async Task Noop_does_not_mark_an_imported_row_as_staff_or_change_its_revision()
    {
        using var tenant = Tenant(); var id = await Seed(); string revision = "";
        await WithUnitOfWorkAsync(async () => revision = (await Service().GetRowAsync(id)).Revision);
        var row = await Service().UpdateAsync(new(id, revision, ChapelAttendanceStatus.Absent, " Scan In Only "));
        Assert.Equal(ChapelAttendanceSource.LegacyImport, row.Source); Assert.Equal(revision, row.Revision);
    }
    [Fact]
    public async Task New_revision_is_returned_and_a_stale_browser_cannot_overwrite_it()
    {
        using var tenant = Tenant(); var id = await Seed(); string revision = "";
        await WithUnitOfWorkAsync(async () => revision = (await Service().GetRowAsync(id)).Revision);
        var first = await Service().UpdateAsync(new(id, revision, ChapelAttendanceStatus.Present, "Staff checked"));
        Assert.NotEqual(revision, first.Revision); Assert.Equal(ChapelAttendanceSource.Staff, first.Source);
        await Assert.ThrowsAsync<UserFriendlyException>(() => Service().UpdateAsync(new(id, revision, ChapelAttendanceStatus.Excused, "Stale edit")));
        await WithUnitOfWorkAsync(async () => Assert.Equal("Staff checked", (await Service().GetRowAsync(id)).Comment));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_updates_leave_the_saved_record_unchanged(bool badStatus)
    {
        using var tenant = Tenant(); var id = await Seed(); ChapelAttendanceRow original = null!;
        await WithUnitOfWorkAsync(async () => original = await Service().GetRowAsync(id));
        await Assert.ThrowsAsync<UserFriendlyException>(() => Service().UpdateAsync(new(id, original.Revision,
            badStatus ? (ChapelAttendanceStatus)99 : ChapelAttendanceStatus.Present, badStatus ? "Note" : new string('x', 4001))));
        await WithUnitOfWorkAsync(async () => Assert.Equal(original, await Service().GetRowAsync(id)));
    }
    [Fact]
    public async Task Foreign_tenant_cannot_read_or_change_a_row()
    {
        Guid id; using (Tenant()) id = await Seed();
        using var other = GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(), "nelson");
        await WithUnitOfWorkAsync(async () => { await Assert.ThrowsAnyAsync<Exception>(() => Service().GetRowAsync(id)); });
        await Assert.ThrowsAnyAsync<Exception>(() => Service().UpdateAsync(new(id, "x", ChapelAttendanceStatus.Present, null)));
    }
    [Fact]
    public async Task Dorm_filter_joins_on_student_ID_and_keeps_term_scope()
    {
        using var tenant = Tenant(); await Seed(); await Seed("002"); await Seed("001", 269);
        _dorms.GetResidencesAsync(268, default).Returns(new ChapelResidence[] { new("001", "Test Student", "bridges"), new("002", "Other", "kendrick") });
        await WithUnitOfWorkAsync(async () =>
        { var rows = await Service().SearchAsync(268, null, null, null, "bridges"); Assert.Equal("001", Assert.Single(rows).StudentId); Assert.All(rows, x => Assert.Equal(268, x.TermCalendarId)); });
    }
    [Fact]
    public async Task Missing_housing_is_unknown_and_never_becomes_off_campus()
    {
        using var tenant = Tenant(); await Seed(); _dorms.GetResidencesAsync(268, default).Returns(Array.Empty<ChapelResidence>());
        await WithUnitOfWorkAsync(async () => { Assert.Empty(await Service().SearchAsync(268, null, null, null, "off-campus")); Assert.Single(await Service().SearchAsync(268, null, null, null, "unknown")); });
    }
    [Fact]
    public async Task Dorm_roster_includes_residents_without_chapel_records_and_pages_without_truncating_the_total()
    {
        using var tenant = Tenant(); await Seed("001");
        _dorms.GetResidencesAsync(268, default).Returns(Enumerable.Range(1, 105).Select(x => new ChapelResidence(x.ToString("000"), $"Student {x:000}", "savell")).ToArray());
        await WithUnitOfWorkAsync(async () =>
        {
            var first = await Service().GetDormRosterAsync(268, "savell"); var second = await Service().GetDormRosterAsync(268, "savell", 2);
            Assert.Equal(105, first.Total); Assert.Equal(100, first.Students.Count); Assert.Equal(5, second.Students.Count);
            Assert.True(first.Students[0].HasChapelRecords); Assert.False(first.Students[1].HasChapelRecords);
            Assert.Empty(first.Students.Select(x => x.StudentId).Intersect(second.Students.Select(x => x.StudentId)));
        });
    }
    [Fact]
    public async Task Other_institution_cannot_query_Nelson_dorm_information()
    {
        using var tenant = GetRequiredService<ICurrentTenant>().Change(_tenant, "other");
        await Assert.ThrowsAsync<UserFriendlyException>(() => Service().GetDormRosterAsync(268, "bridges"));
        await _dorms.DidNotReceiveWithAnyArgs().GetResidencesAsync(default);
    }
}
