using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Xunit;
namespace CampusFlow.MealPlanTaxes;
public class MealPlanTaxCoordinatorTests
{
    private static readonly MealPlanTaxTarget Target=new(10,269,42,8,2475m);
    private static MealPlanTaxCoordinator Service(FakeGateway gateway,FakeStore store,string name="nelson")
    {var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(Guid.NewGuid());tenant.Name.Returns(name);return new(gateway,store,tenant,Microsoft.Extensions.Logging.Abstractions.NullLogger<MealPlanTaxCoordinator>.Instance);}
    [Fact]public async Task Repeated_reviews_post_once_and_verify_the_same_reference()
    {var g=new FakeGateway();var s=new FakeStore();var service=Service(g,s);await service.EnsureForBillReviewAsync("10","269");await service.EnsureForBillReviewAsync("10","269");Assert.Equal(1,g.Posts);Assert.Equal(2,s.Verified);Assert.Equal(2,g.ReportingChecks);}
    [Theory][InlineData(false)][InlineData(true)]public async Task Unknown_post_is_never_resent_and_can_be_recovered_by_its_exact_reference(bool committed)
    {var g=new FakeGateway{LoseResponse=true,CommitBeforeFailure=committed};var s=new FakeStore();var service=Service(g,s);await Assert.ThrowsAsync<UserFriendlyException>(()=>service.EnsureForBillReviewAsync("10","269"));Assert.True(s.Uncertain);g.LoseResponse=false;if(committed)await service.EnsureForBillReviewAsync("10","269");else await Assert.ThrowsAsync<UserFriendlyException>(()=>service.EnsureForBillReviewAsync("10","269"));Assert.Equal(1,g.Posts);Assert.Equal(committed?1:0,s.Verified);}
    [Fact]public async Task Reporting_lag_blocks_review_and_later_confirms_without_a_second_post()
    {var g=new FakeGateway{ReportingUnavailable=true};var s=new FakeStore();var service=Service(g,s);await Assert.ThrowsAsync<UserFriendlyException>(()=>service.EnsureForBillReviewAsync("10","269"));g.ReportingUnavailable=false;await service.EnsureForBillReviewAsync("10","269");Assert.Equal(1,g.Posts);Assert.Equal(1,s.Verified);}
    [Fact]public async Task Another_tenant_cannot_read_or_post_Nelson_tax()
    {var g=new FakeGateway();var s=new FakeStore();await Assert.ThrowsAsync<UserFriendlyException>(()=>Service(g,s,"other").EnsureForBillReviewAsync("10","269"));Assert.Equal(0,g.Reads);Assert.Equal(0,g.Posts);}
    [Fact]public async Task Out_of_scope_target_cannot_be_claimed_or_sent()
    {var g=new FakeGateway{TargetOverride=Target with{StudentUid=11}};var s=new FakeStore();await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(g,s).EnsureForBillReviewAsync("10","269"));Assert.Equal(0,s.Claims);Assert.Equal(0,g.Posts);}
    [Fact]public async Task No_meal_plans_do_not_create_tax_attempts()
    {var g=new FakeGateway{NoTargets=true};var s=new FakeStore();await Service(g,s).EnsureForBillReviewAsync("10","269");Assert.Equal(0,s.Claims);Assert.Equal(0,g.Posts);}
    [Fact]public async Task A_competing_review_cannot_send_the_owners_request()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var g=new FakeGateway{BeforePost=async()=>{entered.SetResult();await release.Task;}};var s=new FakeStore();var service=Service(g,s);
        var first=service.EnsureForBillReviewAsync("10","269");await entered.Task;
        await Assert.ThrowsAsync<UserFriendlyException>(()=>service.EnsureForBillReviewAsync("10","269"));release.SetResult();await first;Assert.Equal(1,g.Posts);Assert.Equal(1,s.Verified);
    }
    [Fact]public async Task A_read_only_preflight_failure_does_not_create_or_consume_a_send_attempt()
    {
        var g=new FakeGateway{ReadUnavailable=true};var s=new FakeStore();var service=Service(g,s);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EnsureForBillReviewAsync("10","269"));Assert.Equal(0,s.Claims);Assert.Equal(0,g.Posts);
        g.ReadUnavailable=false;await service.EnsureForBillReviewAsync("10","269");Assert.Equal(1,g.Posts);Assert.Equal(1,s.Verified);
    }
    private sealed class FakeStore:IMealPlanTaxChargeStore
    {
        private int claimed;public int Claims,Verified;public bool Uncertain;
        public Task<MealPlanTaxClaim> ClaimAsync(MealPlanTaxTarget t,CancellationToken ct=default){Claims++;return Task.FromResult(new MealPlanTaxClaim(Guid.NewGuid(),Interlocked.CompareExchange(ref claimed,1,0)==0));}
        public Task VerifyAsync(Guid id,string entry,CancellationToken ct=default){Verified++;return Task.CompletedTask;}
        public Task MarkUncertainAsync(Guid id,CancellationToken ct=default){Uncertain=true;return Task.CompletedTask;}
    }
    private sealed class FakeGateway:IElementsMealPlanTaxGateway
    {
        public bool NoTargets,LoseResponse,CommitBeforeFailure,ReportingUnavailable,ReadUnavailable;public int Posts,Reads,ReportingChecks;public MealPlanTaxTarget? TargetOverride;public Func<Task>? BeforePost;private string? entry;
        public Task<IReadOnlyList<MealPlanTaxTarget>> TargetsAsync(int student,int term,CancellationToken ct=default){Reads++;return Task.FromResult<IReadOnlyList<MealPlanTaxTarget>>(NoTargets?[]:[TargetOverride??Target]);}
        public Task<string?> FindAsync(MealPlanTaxTarget t,CancellationToken ct=default){if(ReadUnavailable)throw new InvalidOperationException("Preflight unavailable");return Task.FromResult(entry);}
        public async Task CreateAsync(MealPlanTaxTarget t,CancellationToken ct=default){Posts++;if(BeforePost is not null)await BeforePost();if(LoseResponse){if(CommitBeforeFailure)entry="Batch:123";throw new HttpRequestException("Unknown response");}entry="Batch:123";}
        public Task ConfirmReportingAsync(MealPlanTaxTarget t,CancellationToken ct=default){ReportingChecks++;if(ReportingUnavailable)throw new InvalidOperationException("Reporting not ready");return Task.CompletedTask;}
    }
}
