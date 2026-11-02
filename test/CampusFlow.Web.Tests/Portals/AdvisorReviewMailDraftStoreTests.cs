using System;
using System.Threading;
using System.Threading.Tasks;
using CampusFlow.AdvisorPortal;
using CampusFlow.Web.Portals;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CampusFlow.Portals;

public class AdvisorReviewMailDraftStoreTests
{
    [Fact]
    public async Task Consent_Draft_Preserves_Decisions_And_Notes_Once()
    {
        var store = NewStore();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var input = new SubmitAdvisorReviewInput
        {
            StudentProfileId = Guid.NewGuid(),
            ExternalTermId = "2027-SPRING",
            OverallComment = "Please discuss the remaining course.",
            Courses = [new AdvisorCourseDecisionInput
            {
                ReviewId = reviewId, Decision = "Approve", Comment = "Looks good."
            }]
        };

        var id = await store.SaveAsync(userId, tenantId, input, CancellationToken.None);
        var restored = await store.TakeAsync(id, userId, tenantId, CancellationToken.None);

        restored.ShouldNotBeNull();
        restored.StudentProfileId.ShouldBe(input.StudentProfileId);
        restored.ExternalTermId.ShouldBe(input.ExternalTermId);
        restored.OverallComment.ShouldBe(input.OverallComment);
        restored.Courses.Count.ShouldBe(1);
        restored.Courses[0].ReviewId.ShouldBe(reviewId);
        restored.Courses[0].Decision.ShouldBe("Approve");
        restored.Courses[0].Comment.ShouldBe("Looks good.");
        (await store.TakeAsync(id, userId, tenantId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Consent_Draft_Cannot_Be_Taken_By_Another_User_Or_Tenant()
    {
        var store = NewStore();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var id = await store.SaveAsync(userId, tenantId,
            new SubmitAdvisorReviewInput { ExternalTermId = "2027-SPRING" }, CancellationToken.None);

        (await store.TakeAsync(id, Guid.NewGuid(), tenantId, CancellationToken.None)).ShouldBeNull();
        (await store.TakeAsync(id, userId, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        (await store.TakeAsync(id, userId, tenantId, CancellationToken.None)).ShouldNotBeNull();
    }

    private static AdvisorReviewMailDraftStore NewStore() => new(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
}
