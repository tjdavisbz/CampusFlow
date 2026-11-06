using System;
using System.Threading.Tasks;
using CampusFlow.Branding;
using CampusFlow.Chapel;
using CampusFlow.Web.Pages.Admin.Chapel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace CampusFlow.Pages;

public class ChapelAutosaveHandlerTests
{
    private static AttendanceModel Page(string institution = "nelson")
    {
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Name.Returns(institution);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<ICurrentTenant>().Returns(tenant);
        // Null service intentionally fails if invalid input reaches persistence.
        return new AttendanceModel(null!, [], Substitute.For<ITenantThemeProvider>(), NullLogger<AttendanceModel>.Instance)
        { LazyServiceProvider = lazy, PageContext = new PageContext { HttpContext = new DefaultHttpContext() } };
    }
    [Theory]
    [InlineData("missing-status")]
    [InlineData("bad-status")]
    [InlineData("missing-id")]
    [InlineData("missing-revision")]
    [InlineData("long-comment")]
    [InlineData("binding-error")]
    public async Task Invalid_input_is_rejected_before_persistence(string scenario)
    {
        var page = Page(); if (scenario == "binding-error") page.ModelState.AddModelError("status", "Invalid");
        var result = await page.OnPostAutoSaveAsync(scenario == "missing-id" ? Guid.Empty : Guid.NewGuid(),
            scenario == "missing-revision" ? "" : "v1", scenario == "missing-status" ? null : scenario == "bad-status" ? (ChapelAttendanceStatus)99 : ChapelAttendanceStatus.Present,
            scenario == "long-comment" ? new string('x', 4001) : "Note");
        Assert.Equal(400, Assert.IsType<JsonResult>(result).StatusCode);
    }
    [Fact]
    public async Task Other_institution_cannot_use_Nelson_workspace_handlers()
    {
        var page = Page("other");
        Assert.IsType<NotFoundResult>(await page.OnPostAutoSaveAsync(Guid.NewGuid(), "v1", ChapelAttendanceStatus.Present, null));
        Assert.IsType<NotFoundResult>(await page.OnGetAttendanceRowAsync(Guid.NewGuid()));
        Assert.IsType<NotFoundResult>(await page.OnGetDaySummaryAsync(268, new DateTime(2026, 10, 1)));
    }
}
